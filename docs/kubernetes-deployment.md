# Deploying M3A to a local kind cluster

Runbook for running the M3A API and its PostgreSQL database on a local
[kind](https://kind.sigs.k8s.io/) cluster, with CPU-based autoscaling for load testing.

**This is a local development setup, not a production deployment.** Database passwords are
hard-coded (`password`), the database has a single replica with node-local storage, and the
Metrics Server runs with kubelet TLS verification disabled.

Contents: [What gets deployed](#what-gets-deployed) · [Prerequisites](#prerequisites) ·
[Quick start](#quick-start) · [Step by step](#step-by-step) · [Verify](#verify) ·
[Autoscaling and load testing](#autoscaling-and-load-testing) · [Day-to-day tasks](#day-to-day-tasks) ·
[Troubleshooting](#troubleshooting) · [Teardown](#teardown) · [File reference](#file-reference)

---

## What gets deployed

```
 Your machine                     kind node (kind-control-plane)
 ─────────────                    ──────────────────────────────────────────────────────
                                  namespace m3a
 127.0.0.1:8080 ──► NodePort 30080 ──► Service m3a-api ──► Deployment m3a-api (1–3 Pods)
                                                              │  HPA m3a-api: 40% CPU
                                                              │  config: ConfigMap m3a-app-config
                                                              │          Secret    m3a-app-db
                                                              ▼
                                       Service m3a-db (headless) ──► StatefulSet m3a-db (1 Pod)
                                                                       │ PVC data-m3a-db-0 (1Gi)
                                                                       │ init: ConfigMap m3a-db-init
                                                                       │ superuser: Secret m3a-db-superuser
                                  namespace kube-system
                                       Deployment metrics-server  (feeds the HPA)
```

| Component | Name | Defined in |
|---|---|---|
| Cluster + host port mappings | `kind` | `kind.yaml` |
| API image | `localhost/m3a-api:dev` | `Containerfile.api`, `.containerignore` |
| Database schema + roles | `m3a_db`, `m3a_admin`, `m3a_svc` | `db/init.sql` |
| Database | StatefulSet + headless Service `m3a-db` | `db/postgres.yml` |
| App configuration | ConfigMap `m3a-app-config`, Secret `m3a-app-db` | `kubernetes/app-config.yml` |
| API | Deployment + NodePort Service `m3a-api` | `kubernetes/api.yml` |
| Autoscaler | HPA `m3a-api` | `kubernetes/api-hpa.yml` |

M3A has no message broker and no background consumer, so unlike the Tournaments reference
setup there is no ActiveMQ or consumer Deployment here. Optional monitoring (Prometheus, Grafana,
Tempo traces, PostgreSQL query statistics) is covered separately in [monitoring.md](monitoring.md). (`Containerfile.consumer`
is a leftover from that setup and does not build against this repository.)

### Host ports

`kind.yaml` publishes three NodePorts on `127.0.0.1` only:

| Host | NodePort | Used by |
|---|---|---|
| `127.0.0.1:8080` | `30080` | `m3a-api` |
| `127.0.0.1:3000` | `30030` | Grafana, once installed ([monitoring.md](monitoring.md)) |
| `127.0.0.1:9090` | `30090` | Prometheus, once installed ([monitoring.md](monitoring.md)) |

Port mappings are fixed when the cluster is created; changing `kind.yaml` requires recreating
the cluster.

---

## Prerequisites

| Tool | Tested with | Notes |
|---|---|---|
| kind | — | `kind version` |
| kubectl | client matching server v1.37 | `kubectl version` |
| Podman and/or Docker | Podman 6.1, Docker 29.1 (Rancher Desktop) | Images are built with Podman; Docker variants are noted |
| openssl | — | generates the database superuser password |
| curl | — | verification |

Run every command **from the repository root**. Host ports 8080, 3000 and 9090 must be free.

---

## Quick start

For a fresh machine, the whole sequence. Each step is explained in [Step by step](#step-by-step).

```sh
# 1. Cluster
kind create cluster --config kind.yaml

# 2. Metrics Server (once per cluster)
kubectl apply -f https://github.com/kubernetes-sigs/metrics-server/releases/download/v0.8.0/components.yaml
kubectl -n kube-system patch deployment metrics-server --type=json \
  -p='[{"op":"add","path":"/spec/template/spec/containers/0/args/-","value":"--kubelet-insecure-tls"}]'
kubectl -n kube-system rollout status deployment/metrics-server --timeout=180s

# 3. Database
kubectl create namespace m3a --dry-run=client -o yaml | kubectl apply -f -
kubectl -n m3a get secret m3a-db-superuser >/dev/null 2>&1 || \
  kubectl -n m3a create secret generic m3a-db-superuser \
    --from-literal=password="$(openssl rand -hex 16)"
kubectl -n m3a create configmap m3a-db-init \
  --from-file=01-m3a.sql=db/init.sql --dry-run=client -o yaml | kubectl apply -f -
kubectl apply -f db/postgres.yml
kubectl -n m3a rollout status statefulset/m3a-db --timeout=180s

# 4. API image
podman build -f Containerfile.api -t localhost/m3a-api:dev .
IMAGE_DIR=$(mktemp -d)
podman save --format docker-archive -o "$IMAGE_DIR/api.tar" localhost/m3a-api:dev
kind load image-archive "$IMAGE_DIR/api.tar" --name kind
rm -r "$IMAGE_DIR"

# 5. API
kubectl apply -f kubernetes/app-config.yml -f kubernetes/api.yml -f kubernetes/api-hpa.yml
kubectl -n m3a rollout status deployment/m3a-api --timeout=180s

# 6. Check
curl http://127.0.0.1:8080/health/ready     # Healthy
```

---

## Step by step

### 1. Create the cluster

```sh
kind create cluster --config kind.yaml
kubectl config current-context              # kind-kind
kubectl get nodes                           # kind-control-plane   Ready
```

`kind.yaml` creates a single control-plane node named `kind-control-plane` and maps the host
ports above. The cluster is named `kind` (the default); every `--name kind` below refers to it.

kind runs the node as a container of whichever provider it detects — **on this machine that is
Docker**, even though images are built with Podman. Commands that reach into the node
(`docker exec kind-control-plane …`, `docker port kind-control-plane`) therefore use `docker`.
To run kind on Podman instead, create the cluster with `KIND_EXPERIMENTAL_PROVIDER=podman`.

### 2. Install Metrics Server

The HPA needs CPU metrics. Install once per cluster:

```sh
kubectl apply -f https://github.com/kubernetes-sigs/metrics-server/releases/download/v0.8.0/components.yaml
kubectl -n kube-system patch deployment metrics-server --type=json \
  -p='[{"op":"add","path":"/spec/template/spec/containers/0/args/-","value":"--kubelet-insecure-tls"}]'
kubectl -n kube-system rollout status deployment/metrics-server --timeout=180s
kubectl wait --for=condition=Available apiservice/v1beta1.metrics.k8s.io --timeout=180s
kubectl top nodes                           # returns CPU/memory once samples arrive
```

- `--kubelet-insecure-tls` is needed because kind's kubelets use self-signed serving
  certificates. **Local only — never in production.**
- Metrics Server v0.8.0 requires Kubernetes 1.31+. For older clusters pick a release from its
  compatibility matrix.
- Applying the patch twice appends the flag twice; check first with
  `kubectl -n kube-system get deploy metrics-server -o jsonpath='{.spec.template.spec.containers[0].args}'`.

### 3. Deploy PostgreSQL

```sh
# Namespace (idempotent)
kubectl create namespace m3a --dry-run=client -o yaml | kubectl apply -f -

# Superuser password: generated, never committed, kept if it already exists
kubectl -n m3a get secret m3a-db-superuser >/dev/null 2>&1 || \
  kubectl -n m3a create secret generic m3a-db-superuser \
    --from-literal=password="$(openssl rand -hex 16)"

# Initialization script, mounted at /docker-entrypoint-initdb.d
kubectl -n m3a create configmap m3a-db-init \
  --from-file=01-m3a.sql=db/init.sql --dry-run=client -o yaml | kubectl apply -f -

# StatefulSet + headless Service
kubectl apply -f db/postgres.yml
kubectl -n m3a rollout status statefulset/m3a-db --timeout=180s
kubectl -n m3a logs m3a-db-0
```

Expected in the log:

```
/usr/local/bin/docker-entrypoint.sh: running /docker-entrypoint-initdb.d/01-m3a.sql
PostgreSQL init process complete; ready for start up.
... database system is ready to accept connections
```

What `db/init.sql` creates:

| Object | Purpose |
|---|---|
| `m3a_admin` / `password` | Owns the tables, may create new ones (DDL) |
| `m3a_svc` / `password` | What the API connects as — `SELECT/INSERT/UPDATE/DELETE` only, no DDL |
| `m3a_db` | The database |
| `venues`, `events`, `tickets` | Exactly the EF Core model (`dotnet ef dbcontext script`) |

Things to know:

- **Do not set `POSTGRES_DB=m3a_db`.** The script creates the database itself and would fail
  on `CREATE DATABASE`.
- **Initialization runs only on the first start with an empty data directory.** Editing
  `init.sql` and re-applying the ConfigMap has no effect on an existing database — see
  [Reset the database](#reset-the-database).
- The project has no EF migrations; `db/init.sql` *is* the schema. Change it together with
  `src/M3A.Repositories/Persistence/Configurations/`.
- Data survives Pod restarts (PVC `data-m3a-db-0`), but **deleting the kind cluster deletes its
  storage. This is not a backup.**

More detail: [`db/README.md`](../db/README.md).

### 4. Build the API image and load it into kind

```sh
podman build -f Containerfile.api -t localhost/m3a-api:dev .

# kind cannot read Podman's image store; export a Docker-format archive and import it.
IMAGE_DIR=$(mktemp -d)
podman save --format docker-archive -o "$IMAGE_DIR/api.tar" localhost/m3a-api:dev
kind load image-archive "$IMAGE_DIR/api.tar" --name kind
rm -r "$IMAGE_DIR"

# Confirm the node has it (the node is a container of kind's provider — Docker here)
docker exec kind-control-plane crictl images | grep m3a-api
```

**With Docker instead:**

```sh
docker build -f Containerfile.api -t localhost/m3a-api:dev .
kind load docker-image localhost/m3a-api:dev --name kind
```

> ⚠️ Docker reads `.dockerignore`, not `.containerignore`. Without a `.dockerignore` the build
> context includes your local `.env` (database credentials). Copy `.containerignore` to
> `.dockerignore` before building with Docker.

How `Containerfile.api` builds:

- **Build stage** (`dotnet/sdk:10.0`) copies `global.json`, `Directory.*.props`, `.editorconfig`
  and the four `src/` project files first and restores, so the NuGet layer is cached until a
  dependency changes; then copies `src/` and publishes `M3A.Api` in Release. Test projects are
  never built into the image.
- **Runtime stage** (`dotnet/aspnet:10.0`) runs as the non-root `app` user, listens on 8080,
  `ASPNETCORE_ENVIRONMENT=Production` — so **Swagger is not available in the cluster**.
- No registry push: `kind load` puts the image straight into the node's image store, and
  `api.yml` uses `imagePullPolicy: Never`.

### 5. Deploy the API

```sh
kubectl apply -f kubernetes/app-config.yml
kubectl apply -f kubernetes/api.yml
kubectl apply -f kubernetes/api-hpa.yml
kubectl -n m3a rollout status deployment/m3a-api --timeout=180s
```

**Configuration.** `src/M3A.Api/appsettings.json` builds its connection string from
`${DB_HOST}`, `${DB_PORT}`, `${DB_NAME}`, `${DB_USERNAME}`, `${DB_PASSWORD}`. In the cluster
these come from environment variables (which always override any `.env`):

| Variable | Value | Source |
|---|---|---|
| `DB_HOST` | `m3a-db.m3a.svc.cluster.local` | ConfigMap `m3a-app-config` |
| `DB_PORT` | `5432` | ConfigMap `m3a-app-config` |
| `DB_NAME` | `m3a_db` | ConfigMap `m3a-app-config` |
| `DB_USERNAME` | `m3a_svc` | ConfigMap `m3a-app-config` |
| `DB_PASSWORD` | `password` | Secret `m3a-app-db` |

The API reaches the database through the cluster DNS name — no port-forward is involved.

**Health probes.** The API exposes two endpoints
(`src/M3A.Api/Extensions/HealthCheckExtensions.cs`):

| Endpoint | Checks | Probe | Effect when failing |
|---|---|---|---|
| `GET /health/live` | nothing — process is serving HTTP | liveness | Pod is restarted |
| `GET /health/ready` | database reachable | readiness | Pod removed from the Service, **not** restarted |

This split means a database outage takes API Pods out of rotation and they rejoin on their own
when the database returns, instead of being restarted in a loop.

**Resources.** Each API Pod requests `250m` CPU / `128Mi` memory and is limited to `512Mi`
memory. The CPU request is what the HPA measures against.

---

## Verify

### API reachable from the host

```sh
curl http://127.0.0.1:8080/health/live      # Healthy
curl http://127.0.0.1:8080/health/ready     # Healthy  (database reachable)
curl http://127.0.0.1:8080/venues           # []  on a fresh database
```

### End-to-end through the database

```sh
B=http://127.0.0.1:8080

# Create a venue, then an event (creating an event issues eventCapacity tickets)
curl -s -X POST $B/venues -H 'Content-Type: application/json' \
  -d '{"location":"Arena CDMX","capacity":100}'
#   -> {"id":"<venueId>",...}   HTTP 201

curl -s -X POST $B/events -H 'Content-Type: application/json' \
  -d '{"name":"Smoke test","dateTime":"2027-03-01T20:00:00Z","eventCapacity":3,"venueId":"<venueId>"}'
#   -> {"id":"<eventId>",...}   HTTP 201

curl -s $B/tickets                                       # three tickets, status Issued
curl -s -X POST $B/tickets/<ticketId>/purchase           # status Purchased, HTTP 200
```

### Rows in PostgreSQL

```sh
kubectl -n m3a exec -it m3a-db-0 -- env PGPASSWORD=password \
  psql -U m3a_svc -d m3a_db -h 127.0.0.1 -c 'select "Status", count(*) from tickets group by 1'
```

### Cluster state

```sh
kubectl -n m3a get pods,svc,hpa,pvc
```

Expected: `m3a-api-*` and `m3a-db-0` `1/1 Running` with 0 restarts, `service/m3a-api` as
`NodePort 8080:30080/TCP`, HPA `cpu: <n>%/40%`, PVC `data-m3a-db-0` `Bound`.

### Connecting a desktop SQL client

Only needed for tools on your machine (psql, DBeaver…), not for the API:

```sh
kubectl -n m3a port-forward svc/m3a-db 5432:5432
# host=localhost port=5432 db=m3a_db user=m3a_svc password=password
```

Fails if something on your machine already listens on 5432 — use e.g. `15432:5432` instead.

---

## Autoscaling and load testing

The HPA (`kubernetes/api-hpa.yml`) keeps **1–3** API Pods, targeting **40% of each Pod's
requested CPU** averaged across Pods. With a `250m` request that is about **100m (0.1 core) per
Pod** — not 40% of the host's CPU. Requests are scheduling reservations, not limits.

Watch it:

```sh
kubectl top pods -n m3a
kubectl -n m3a get hpa m3a-api --watch
```

Generate load from inside the cluster (bypasses the host port mapping):

```sh
kubectl -n m3a run m3a-load --image=busybox:1.37 --restart=Never -- /bin/sh -c \
  'while true; do wget -q -O /dev/null http://m3a-api.m3a.svc.cluster.local:8080/events; done'

kubectl -n m3a get hpa m3a-api --watch      # Ctrl+C when done

kubectl -n m3a delete pod m3a-load --now
```

Observed on this setup: a single load Pod scaled the API from 1 to 3 replicas within about 30
seconds.

For a realistic mix that exercises every endpoint end to end, use the Locust suite instead:
[`loadtests/README.md`](../loadtests/README.md).

Things to know:

- Metrics need a minute or so after deploy; until then the HPA shows `<unknown>/40%`.
- **Scale-down is slow by design** — the default stabilization window is 5 minutes.
- Only Pods scale; the single kind node must have room for 3 × 250m CPU of requests.
- Re-applying `kubernetes/api.yml` resets the Deployment to 1 replica until the HPA acts again.
- For repeatable load tests start from an empty database ([reset](#reset-the-database)) —
  `GET /events` gets slower as data accumulates.

---

## Day-to-day tasks

### Deploy a code change

The tag stays `:dev`, so Kubernetes will not notice a new image by itself — restart the
Deployment after loading it:

```sh
podman build -f Containerfile.api -t localhost/m3a-api:dev .
IMAGE_DIR=$(mktemp -d)
podman save --format docker-archive -o "$IMAGE_DIR/api.tar" localhost/m3a-api:dev
kind load image-archive "$IMAGE_DIR/api.tar" --name kind
rm -r "$IMAGE_DIR"
kubectl -n m3a rollout restart deployment/m3a-api
kubectl -n m3a rollout status deployment/m3a-api --timeout=180s
```

### Change configuration

Edit `kubernetes/app-config.yml`, apply it, then restart — environment variables are read
only at Pod start:

```sh
kubectl apply -f kubernetes/app-config.yml
kubectl -n m3a rollout restart deployment/m3a-api
```

### Enable Swagger in the cluster

Add to the `api` container in `kubernetes/api.yml`, then apply:

```yaml
          env:
            - name: ASPNETCORE_ENVIRONMENT
              value: Development
```

Swagger is then at `http://127.0.0.1:8080/swagger`.

### Reset the database

Deletes all data and re-runs `db/init.sql`:

```sh
kubectl -n m3a create configmap m3a-db-init \
  --from-file=01-m3a.sql=db/init.sql --dry-run=client -o yaml | kubectl apply -f -
kubectl -n m3a delete statefulset m3a-db
kubectl -n m3a delete pvc data-m3a-db-0
kubectl apply -f db/postgres.yml
kubectl -n m3a rollout status statefulset/m3a-db --timeout=180s
```

API Pods go unready while the database is down and recover on their own.

### Logs

```sh
kubectl -n m3a logs deploy/m3a-api -f
kubectl -n m3a logs m3a-db-0 -f
```

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| API Pod `ErrImageNeverPull` | Image not loaded into kind | Run [step 4](#4-build-the-api-image-and-load-it-into-kind); check `crictl images` on the node |
| API Pod `0/1 Running`, never ready | `/health/ready` failing — database unreachable | `kubectl -n m3a get pods` for `m3a-db-0`; `curl` the endpoint via `kubectl -n m3a port-forward deploy/m3a-api 18080:8080` |
| API Pod `CrashLoopBackOff`, log names missing variables | ConfigMap/Secret not applied | `kubectl apply -f kubernetes/app-config.yml`, then restart the Deployment |
| `m3a-db-0` `CreateContainerConfigError` | Secret `m3a-db-superuser` or ConfigMap `m3a-db-init` missing | Re-run [step 3](#3-deploy-postgresql) |
| DB log `ERROR: role "m3a_svc" already exists` / tables missing after editing `init.sql` | Init only runs on an empty volume | [Reset the database](#reset-the-database) |
| `password authentication failed for user "m3a_svc"` | Password in `app-config.yml` differs from `init.sql` | Make them match; reset the database if `init.sql` changed |
| `curl 127.0.0.1:8080` refused | Cluster created without `kind.yaml`, or Service `nodePort` ≠ 30080 | `docker port kind-control-plane` should list `30080/tcp -> 127.0.0.1:8080`; otherwise recreate the cluster |
| HPA shows `<unknown>/40%` | Metrics Server missing or still warming up | Wait a minute; `kubectl top pods -n m3a`; check step 2 |
| New code not running after rebuild | Same `:dev` tag, Pods not restarted | `kubectl -n m3a rollout restart deployment/m3a-api` |
| `curl` returns `000` / connection reset briefly | Request hit a Pod during a rollout | Retry; transient |

---

## Teardown

Remove the application, keep the cluster:

```sh
kubectl delete -f kubernetes/api-hpa.yml -f kubernetes/api.yml -f kubernetes/app-config.yml
kubectl delete -f db/postgres.yml
kubectl -n m3a delete pvc data-m3a-db-0                 # deletes the data
kubectl delete namespace m3a                            # also removes Secrets/ConfigMaps
```

Remove everything, including all data:

```sh
kind delete cluster --name kind
```

---

## File reference

| File | What it is |
|---|---|
| `kind.yaml` | Cluster definition: one control-plane node, host ports 8080/3000/9090 → NodePorts 30080/30030/30090 on `127.0.0.1` |
| `Containerfile.api` | Two-stage build of `src/M3A.Api` (SDK → ASP.NET runtime), non-root, port 8080 |
| `.containerignore` | Keeps `.env`, `bin/`, `obj/`, `.git` and IDE folders out of the build context (Podman/Buildah) |
| `db/init.sql` | Roles, database and tables; mirrors the EF Core model |
| `db/postgres.yml` | PostgreSQL 17 StatefulSet with a 1Gi PVC and headless Service `m3a-db` |
| `db/README.md` | Database-specific notes |
| `kubernetes/app-config.yml` | `DB_*` settings (ConfigMap) and `DB_PASSWORD` (Secret) for the API |
| `kubernetes/api.yml` | API Deployment (probes, resources, OTLP trace endpoint) and NodePort Service on 30080 |
| `kubernetes/api-hpa.yml` | HPA: 1–3 replicas at 40% CPU |
| `src/M3A.Api/Extensions/HealthCheckExtensions.cs` | `/health/live` and `/health/ready` |
| `src/M3A.Api/Extensions/TelemetryExtensions.cs` | OpenTelemetry: Prometheus `/metrics`, OTLP traces when configured |
| `kubernetes/monitoring/` | Prometheus/Grafana Helm values, ServiceMonitors, Tempo, postgres_exporter — see [monitoring.md](monitoring.md) |
