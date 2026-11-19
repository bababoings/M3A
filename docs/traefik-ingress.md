# Optional: expose the API through Traefik on kind

An alternative to the direct API NodePort mapping in
[kubernetes-deployment.md](kubernetes-deployment.md). It uses the standard Kubernetes
**Ingress** API, a Traefik controller and hostname routing. Requires Helm 3+, `kind`, `kubectl`
and the container runtime your cluster uses. Run from the repository root. Nothing is installed
automatically.

```text
http://m3a.local:8080
  → host 127.0.0.1:8080 → kind node :30081
  → Traefik "web" entrypoint → Ingress m3a-api → Service m3a-api:8080 → ready API Pods
```

**Why bother:** a NodePort Service balances per **TCP connection** (kube-proxy picks a Pod once,
when the connection opens), which is why the 10-user load tests pinned 99% of traffic to one Pod
until Locust was changed to reconnect (see `loadtests/README.md`). Traefik keeps its own
connections to every ready Pod and balances per **request**, so keep-alive clients spread across
Pods the HPA adds mid-run.

| File | Purpose |
|---|---|
| [`kind-traefik.yaml`](../kind-traefik.yaml) | Cluster config: host 8080 → Traefik NodePort 30081 |
| [`kubernetes/traefik/values.yml`](../kubernetes/traefik/values.yml) | Helm values (chart 42.x): NodePort 30081, HTTP only, explicit `traefik` IngressClass, no dashboard |
| [`kubernetes/traefik/api-ingress.yml`](../kubernetes/traefik/api-ingress.yml) | `m3a.local`, prefix `/` → `m3a-api:8080` (standard Ingress; Traefik's default round-robin) |
| [`kubernetes/traefik/api-ingressroute-p2c.yml`](../kubernetes/traefik/api-ingressroute-p2c.yml) | **Recommended.** The same route as a Traefik `IngressRoute` with `strategy: p2c` (sends each request to the less busy of two Pods). Apply **instead of** `api-ingress.yml`, not alongside it |

**Use `p2c`.** In the stress tests, round-robin (the plain Ingress) kept sending a CPU-throttled Pod
its full third of the traffic, so the deployment broke at ~400 req/s. With `p2c` it reached
~740 req/s at p95 316 ms on the same cluster. See
`loadtests/results/run-2026-11-18-stress-traefik-p2c/REPORT.md`. `IngressRoute` needs the CRD provider,
which `values.yml` enables.

```bash
kubectl delete -f kubernetes/traefik/api-ingress.yml --ignore-not-found
kubectl apply -f kubernetes/traefik/api-ingressroute-p2c.yml
```

## 1. Create a cluster with the ingress port mapping

Use `kind-traefik.yaml` **instead of** `kind.yaml`. Both use host port 8080, but this one sends it
to Traefik's NodePort `30081`, not the API's `30080`. Ports 3000 and 9090 stay mapped for
monitoring. All three host ports must be free, so this cluster and one created from `kind.yaml`
cannot run at the same time.

**Bash / zsh:**

```bash
export KIND_CLUSTER=m3a-ingress
kind create cluster --name "$KIND_CLUSTER" --config kind-traefik.yaml
kubectl config use-context "kind-${KIND_CLUSTER}"
```

**Fish:**

```fish
set -gx KIND_CLUSTER m3a-ingress
kind create cluster --name "$KIND_CLUSTER" --config kind-traefik.yaml
kubectl config use-context "kind-$KIND_CLUSTER"
```

kind cannot add port mappings to a running cluster. **Do not delete an existing cluster** without
backing up the database: deleting it removes the PostgreSQL volume. To keep an existing cluster
running alongside, change the `hostPort` values in `kind-traefik.yaml` before creating it and adjust
the URLs below. To add Traefik to the **existing** cluster instead, skip this section and use the
port-forward in [section 4](#port-forward-existing-cluster).

Then follow steps 2–5 of [kubernetes-deployment.md](kubernetes-deployment.md#step-by-step)
(Metrics Server, PostgreSQL, API image, API) on this cluster. Skip its step 1 (cluster creation).
Two changes: the image load needs `--name "$KIND_CLUSTER"` instead of `--name kind`, and the
direct `curl http://127.0.0.1:8080/...` checks no longer reach the API (port 8080 now goes to
Traefik). The Deployment, HPA and database configuration are unchanged.

## 2. Install Traefik

```bash
helm repo add traefik https://traefik.github.io/charts
helm repo update traefik
helm search repo traefik/traefik --versions | head
```

Pick a chart version compatible with your Kubernetes version. `values.yml` was written and tested
against **42.4.1** (Traefik v3.7). It sets the Service type via `service.spec.type` and the port via
`ports.web.nodePort`. Check `helm show values` if you pick a different major version.

```bash
# Bash
read -r -p 'Compatible Traefik chart version: ' TRAEFIK_CHART_VERSION
# zsh
read -r 'TRAEFIK_CHART_VERSION?Compatible Traefik chart version: '
# fish
read --prompt-str 'Compatible Traefik chart version: ' TRAEFIK_CHART_VERSION
```

```bash
helm upgrade --install traefik traefik/traefik \
  --version "$TRAEFIK_CHART_VERSION" \
  --namespace traefik --create-namespace \
  --values kubernetes/traefik/values.yml \
  --wait --timeout 5m
kubectl -n traefik rollout status deployment/traefik --timeout=180s
kubectl get ingressclass traefik
kubectl -n traefik get service traefik     # NodePort 80:30081/TCP
```

The `traefik` IngressClass is explicit and **not** the cluster default, so Ingresses without
`ingressClassName: traefik` are ignored. The dashboard is not exposed, and HTTPS (`websecure`) is
turned off. Installing the controller does not enable HTTPS.

## 3. Apply the application route

After the API Service exists:

```bash
kubectl apply -f kubernetes/traefik/api-ingress.yml
kubectl -n m3a get ingress m3a-api
```

It routes host `m3a.local`, path prefix `/` (path preserved), to `m3a-api:8080` in namespace `m3a`.
An Ingress neither configures DNS nor installs a controller.

The API Service stays `NodePort`, which keeps direct access available, but Traefik reaches it
internally and does not use its NodePort. For an ingress-only setup you can convert it:

```bash
kubectl -n m3a patch service m3a-api --type=strategic \
  -p '{"spec":{"type":"ClusterIP","ports":[{"port":8080,"nodePort":null}]}}'
```

To make that permanent, also set `type: ClusterIP` and remove `nodePort` in `kubernetes/api.yml`,
or reapplying it restores the NodePort. Do not change the Deployment or Service selectors.

## 4. Access and verify the API

Add to your hosts file (usually `/etc/hosts`), using your usual admin workflow:

```text
127.0.0.1 m3a.local
```

Open <http://m3a.local:8080/swagger> (Development only) or call the endpoints. A 404 at `/` is
expected. To test without editing hosts:

```bash
curl --fail --resolve m3a.local:8080:127.0.0.1 http://m3a.local:8080/health/ready   # Healthy
curl --fail --resolve m3a.local:8080:127.0.0.1 http://m3a.local:8080/venues
```

`http://localhost:8080` and `http://127.0.0.1:8080` do not match the host rule (Traefik answers
`404`). Use `m3a.local` or pass `-H 'Host: m3a.local'`. For Locust:

```bash
.venv-loadtests/bin/locust -f loadtests/locustfile.py --host http://m3a.local:8080   # needs the hosts entry
```

Prometheus' ServiceMonitor scrapes the API Service internally and needs no change. Grafana and
Prometheus stay on ports 3000 and 9090 and are not routed through Traefik.

<a id="port-forward-existing-cluster"></a>
**Existing cluster without the 30081 mapping** (e.g. one created from `kind.yaml`): install
Traefik (section 2) and the Ingress (section 3) there, then forward a free local port in another
terminal. Port 8080 is already taken by `kind.yaml`'s API mapping, so use 8081:

```bash
kubectl -n traefik port-forward service/traefik 8081:80
curl --fail -H 'Host: m3a.local' http://127.0.0.1:8081/health/ready
```

This exercises Traefik routing, but `kubectl port-forward` is a single slow tunnel. Do not
load-test through it.

## Troubleshooting

```bash
kubectl -n traefik logs deployment/traefik --tail=100
kubectl -n m3a describe ingress m3a-api
kubectl -n m3a get endpointslices -l kubernetes.io/service-name=m3a-api
```

- **Connection refused:** check the cluster's port mappings (`docker port <cluster>-control-plane`)
  and that the Traefik Pod is Ready.
- **Traefik 404:** wrong Host header or IngressClass. Plain `localhost` does not match `m3a.local`.
- **502/503:** check API Pod readiness (`/health/ready` needs the database), the Service selector and
  the backend port name `http`.
- **Empty Ingress ADDRESS:** a NodePort controller publishes no load-balancer address. Test the
  routing instead of relying on this field.
- **Helm schema error:** compare with `helm show values traefik/traefik --version
  "$TRAEFIK_CHART_VERSION"`. Helm values are not resources: do not `kubectl apply` the whole
  `kubernetes/traefik/` directory.

## Production differences

Host port 8080 avoids privileged-port restrictions (e.g. rootless Podman). For port 80, change only
`hostPort` in `kind-traefik.yaml` before creating the cluster; the NodePort stays `30081`. HTTPS also
needs an enabled `websecure` entrypoint, a mapped host port, TLS certificates and Ingress TLS
configuration. Production normally exposes the controller through an external load balancer, uses
real DNS and HTTPS, and keeps application Services `ClusterIP`. Keep this local HTTP setup on
loopback and do not expose it publicly as-is.

To remove it: `kubectl delete -f kubernetes/traefik/api-ingress.yml`, then
`helm uninstall traefik -n traefik` and `kubectl delete namespace traefik`.
