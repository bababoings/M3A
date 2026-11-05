# Find API and PostgreSQL hotspots with Prometheus, Grafana and Tempo

This guide extends the **local kind** deployment from
[kubernetes-deployment.md](kubernetes-deployment.md). Run commands from the repository root
against the same cluster/context as the application. Requires `kubectl`, Helm 3+, the .NET 10
SDK if running the API outside containers, and enough host capacity for Prometheus, Grafana,
Tempo and their supporting Pods. Nothing in this file is installed automatically.

### Shell compatibility

Commands marked `bash` also work in **fish** (and zsh), except for Bash's `read -r -p` prompt
and the SQL heredoc (`<<'SQL'`). Fish alternatives are given beside those examples.
Pipelines, quoted arguments and backslash line continuations work unchanged. Run the examples
in the same shell session so the chart-version variable stays available.

## What each tool can show

| Layer | Measurements | Requirement |
| --- | --- | --- |
| Kubernetes | API/DB CPU, memory, restarts, replica count | Monitoring stack (section 1) |
| API | Slow routes, throughput, HTTP errors, runtime and Npgsql metrics | Built-in instrumentation; scraping in section 3 |
| PostgreSQL | Expensive normalized SQL, calls, average execution time | `pg_stat_statements` + exporter (sections 4–5) |
| Code | Request-to-query correlation | Tempo traces (section 3); a profiler for exact methods/allocations |

The API exposes `/health/live`, `/health/ready` and **`/metrics`**: HTTP request histograms,
.NET runtime metrics, and Npgsql client/pool metrics. Installing Grafana alone does not
configure scraping or PostgreSQL server statistics. Metrics Server, installed for the HPA, is
not a replacement for Prometheus historical metrics.

## 1. Install Prometheus and Grafana

Check the context before installing. The chart installs cluster-wide RBAC and Prometheus
Operator CRDs as well as namespaced workloads:

```bash
kubectl config current-context        # expect kind-<your cluster>
helm repo add prometheus-community https://prometheus-community.github.io/helm-charts
helm repo update
helm search repo prometheus-community/kube-prometheus-stack --versions | head
```

Choose a chart version compatible with your Kubernetes version and record it so the install
is repeatable; the prompt avoids silently tracking `latest`:

```bash
read -r -p 'Compatible kube-prometheus-stack chart version: ' MONITORING_CHART_VERSION
helm upgrade --install monitoring prometheus-community/kube-prometheus-stack \
  --version "$MONITORING_CHART_VERSION" \
  --namespace monitoring --create-namespace --wait --timeout 10m \
  -f kubernetes/monitoring/prometheus-values.yml

kubectl -n monitoring get pods
```

**Fish:** replace only the `read` line with:

```fish
read --prompt-str 'Compatible kube-prometheus-stack chart version: ' MONITORING_CHART_VERSION
```

**zsh** (the macOS default) does not support `read -p` either; use
`read -r 'MONITORING_CHART_VERSION?Compatible kube-prometheus-stack chart version: '`.

The [Helm values](../kubernetes/monitoring/prometheus-values.yml) are local-development
settings, not production sizing. They disable Alertmanager and the kind control-plane
targets that are unreachable by default, and make Prometheus pick up ServiceMonitors from
every namespace. The file is **Helm input, not a Kubernetes resource**: never `kubectl apply`
it or the whole `kubernetes/monitoring/` directory.

Prometheus and Grafana have no persistent storage here: replacing a Pod loses metrics and
hand-made dashboards. Export dashboards (and add PVCs) before relying on retained data. The
stack competes with the API and DB for the kind node's resources; check Pod events if
anything stays `Pending`.

## 2. Open the dashboards

[`kind.yaml`](../kind.yaml) already maps these ports, so with a cluster created from it the
dashboards are reachable as soon as their workloads run:

| Dashboard | Host URL | Service NodePort |
| --- | --- | --- |
| Grafana | <http://127.0.0.1:3000> | `30030` |
| Prometheus | <http://127.0.0.1:9090> | `30090` |

No port-forwards are needed. The mappings bind to IPv4 `127.0.0.1` only, so use that
address rather than `localhost` (see [Troubleshooting](#troubleshooting)). If
monitoring was installed before these values existed, rerun the Helm command in section 1.

**Clusters created from an older `kind.yaml`** keep their original port mappings. Either
recreate the cluster (this **deletes the local database volume**; ensure host ports 3000 and
9090 are free first) or use port-forwards, each in its own terminal:

```bash
kubectl -n monitoring port-forward service/monitoring-grafana 3000:80
```

```bash
kubectl -n monitoring port-forward service/monitoring-kube-prometheus-prometheus 9090:9090
```

Get Grafana's generated admin password; do not commit or share it:

```bash
kubectl -n monitoring get secret monitoring-grafana \
  -o jsonpath='{.data.admin-password}' | base64 --decode; echo
```

Log in to Grafana as `admin`. The chart provisions a Prometheus data source. In Prometheus,
check **Status → Target health** (or **Targets**, depending on the UI version).

Start with the bundled **Kubernetes / Compute Resources / Workload** dashboard, selecting
namespace `m3a` and workload `m3a-api`. It works before API scraping is configured.

## 3. Scrape the API metrics and export traces

OpenTelemetry is configured in
[`TelemetryExtensions.cs`](../src/M3A.Api/Extensions/TelemetryExtensions.cs), with package
versions pinned in `Directory.Packages.props`. HTTP latency uses explicit histogram buckets
suited to the PromQL below. Npgsql 10 exports operation durations, pool connection counts
and other driver metrics through its `Npgsql` meter. The pool is named `m3a-db`
(`DataSourceName` in `M3A.Repositories`), not the connection string, to keep connection
details out of labels.

These are client-side metrics, not PostgreSQL server statistics.

Rebuild and load the API image ([kubernetes-deployment.md, step 4](kubernetes-deployment.md#4-build-the-api-image-and-load-it-into-kind)),
then roll the Deployment and check the endpoint:

```bash
kubectl apply -f kubernetes/api.yml
kubectl -n m3a rollout restart deployment/m3a-api
kubectl -n m3a rollout status deployment/m3a-api --timeout=180s
curl -s http://127.0.0.1:8080/metrics | grep -m3 http_server_request_duration
```

A 404 means the old image is still running.

Apply the [API ServiceMonitor](../kubernetes/monitoring/api-servicemonitor.yml) once the
Prometheus Operator CRDs from section 1 exist:

```bash
kubectl apply -f kubernetes/monitoring/api-servicemonitor.yml
```

`port: http` is the **Service port name**, not a container port number. The ServiceMonitor
scrapes every ready API endpoint individually, not a random Pod through the NodePort. It
matches the `app.kubernetes.io/name: m3a-api` label already on the Service in
`kubernetes/api.yml`. Within a minute, `m3a/m3a-api` should be up in Prometheus targets.

### Export REST and SQL traces to Tempo

ASP.NET Core request spans and Npgsql command spans are registered together, so database
commands nest under the request that issued them. `/health/*` and `/metrics` are excluded
from tracing. The service name is `m3a-api`; override it with `OTEL_SERVICE_NAME`. Tracing
is only enabled when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.

Prometheus stores metrics, **not traces**. The repository includes a single-instance Tempo
and its Grafana datasource:

- [tempo.yml](../kubernetes/monitoring/tempo.yml): config, 5 GiB PVC, Deployment and internal
  Service. Pinned to Tempo 2.10 (2.x single-binary layout); moving to 3.x requires reviewing
  its migration notes.
- [tempo-datasource.yml](../kubernetes/monitoring/tempo-datasource.yml): a datasource
  ConfigMap discovered by the chart's Grafana sidecar.

After section 1 (Bash or fish):

```bash
kubectl apply -f kubernetes/monitoring/tempo.yml
kubectl -n monitoring rollout status deployment/tempo --timeout=180s
kubectl apply -f kubernetes/monitoring/tempo-datasource.yml
```

`kubernetes/api.yml` already sets `OTEL_EXPORTER_OTLP_ENDPOINT` to
`http://tempo.monitoring.svc.cluster.local:4317` with protocol `grpc`; no separate Collector
is needed. (If Tempo is not deployed, the API only logs export failures.) To change a live
Deployment without reapplying the file:

```bash
kubectl -n m3a set env deployment/m3a-api \
  OTEL_EXPORTER_OTLP_ENDPOINT=http://tempo.monitoring.svc.cluster.local:4317 \
  OTEL_EXPORTER_OTLP_PROTOCOL=grpc
kubectl -n m3a rollout status deployment/m3a-api --timeout=180s
```

Grafana queries Tempo on **port 3200**, not the OTLP ingest ports 4317/4318. Never put
`localhost` in a datasource URL — that is Grafana's own Pod. The sidecar reloads datasources
without restarting Grafana and leaves the Prometheus datasource intact.

Open Grafana → **Connections → Data sources → Tempo** → **Save & test**. Then, in
**Explore**, choose **Tempo**, TraceQL mode, last 15 minutes:

```traceql
{ resource.service.name = "m3a-api" }
```

Call `GET /events` or run the [Locust suite](../loadtests/README.md) to generate traffic and
allow a few seconds for the batch exporter. Opening a trace shows the REST request with its
nested Npgsql spans. Spans created before export was enabled are not recoverable.

Tempo is internal-only, without authentication or a NodePort. Its PVC keeps traces across
Pod replacements with 48-hour retention; deleting the PVC or the kind cluster loses them.
Retention is not a size limit — heavy load can fill the 5 GiB volume. This is not a
production HA setup. After editing `tempo-config`, reapply the file and run
`kubectl -n monitoring rollout restart deployment/tempo`.

For a different OTLP receiver, override the environment: HTTP/protobuf uses protocol
`http/protobuf` and usually port 4318. Set `OTEL_TRACES_SAMPLER=parentbased_traceidratio`
and `OTEL_TRACES_SAMPLER_ARG=0.1` to sample ~10% of new root traces while honouring parent
decisions; by default every trace is kept.

For local API development (`dotnet run`), keep this running in another terminal:

```bash
kubectl -n monitoring port-forward service/tempo 4317:4317
```

then, in Bash, zsh or fish:

```bash
env OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317 \
  OTEL_EXPORTER_OTLP_PROTOCOL=grpc dotnet run --project src/M3A.Api
```

Npgsql spans can contain SQL text and database/server metadata. Queries are parameterized and
parameter logging is off, but SQL literals can still be sensitive: restrict backend access
and review telemetry before exporting it elsewhere. Tracing does not replace the server-side
analysis in sections 4–7.

Keep `/metrics` private outside local development. Never use raw URLs, ids, names, passwords
or SQL text as metric labels: they leak data and explode time-series cardinality.

## 4. Enable PostgreSQL query statistics

`pg_stat_statements` records normalized query statistics inside PostgreSQL. Preloading it
requires a **database restart and brief downtime**. Do not delete the PVC or rerun
`db/init.sql`. These changes persist in the existing data directory but must be redone after
a [database reset](kubernetes-deployment.md#reset-the-database).

Inspect the current setting:

```bash
kubectl -n m3a exec m3a-db-0 -- \
  psql -U postgres -d m3a_db -c 'SHOW shared_preload_libraries;'
```

If it is empty (the repository default), run the block below. If other libraries are listed,
keep them and append `pg_stat_statements` instead of overwriting the list.

```bash
kubectl -n m3a exec m3a-db-0 -- \
  psql -U postgres -d m3a_db \
  -c "ALTER SYSTEM SET shared_preload_libraries = 'pg_stat_statements';"
kubectl -n m3a rollout restart statefulset/m3a-db
kubectl -n m3a rollout status statefulset/m3a-db --timeout=180s

kubectl -n m3a exec -i m3a-db-0 -- \
  psql -v ON_ERROR_STOP=1 -U postgres -d m3a_db <<'SQL'
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;
DO $$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'm3a_monitor') THEN
    CREATE ROLE m3a_monitor LOGIN PASSWORD 'password';
  END IF;
END;
$$;
GRANT CONNECT ON DATABASE m3a_db TO m3a_monitor;
GRANT USAGE ON SCHEMA public TO m3a_monitor;
GRANT pg_monitor TO m3a_monitor;
SQL
```

**Fish:** use this instead; `printf` pipes the SQL because fish has no heredocs:

```fish
kubectl -n m3a exec m3a-db-0 -- \
  psql -U postgres -d m3a_db \
  -c "ALTER SYSTEM SET shared_preload_libraries = 'pg_stat_statements';"
kubectl -n m3a rollout restart statefulset/m3a-db
kubectl -n m3a rollout status statefulset/m3a-db --timeout=180s

printf '%s\n' \
  'CREATE EXTENSION IF NOT EXISTS pg_stat_statements;' \
  'DO $$' \
  'BEGIN' \
  "  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'm3a_monitor') THEN" \
  "    CREATE ROLE m3a_monitor LOGIN PASSWORD 'password';" \
  '  END IF;' \
  'END;' \
  '$$;' \
  'GRANT CONNECT ON DATABASE m3a_db TO m3a_monitor;' \
  'GRANT USAGE ON SCHEMA public TO m3a_monitor;' \
  'GRANT pg_monitor TO m3a_monitor;' \
  | kubectl -n m3a exec -i m3a-db-0 -- \
      psql -v ON_ERROR_STOP=1 -U postgres -d m3a_db
```

`m3a_monitor` is separate from the application role `m3a_svc`. `pg_monitor` can see
sensitive server and query activity; use strong credentials and stricter access outside
local development. Query text and statistics are sensitive even when normalized.

## 5. Export PostgreSQL metrics to Prometheus

The [exporter manifest](../kubernetes/monitoring/postgres-exporter.yml) contains the
postgres_exporter Deployment (with the PostgreSQL 17-compatible statement collector enabled),
its Service and ServiceMonitor. Create its Secret first; the password is the **local-only**
one from section 4:

```bash
kubectl -n m3a create secret generic postgres-exporter \
  --from-literal=DATA_SOURCE_NAME='postgresql://m3a_monitor:password@m3a-db:5432/m3a_db?sslmode=disable' \
  --dry-run=client -o yaml | kubectl apply -f -

kubectl apply -f kubernetes/monitoring/postgres-exporter.yml
kubectl -n m3a rollout status deployment/postgres-exporter --timeout=180s
```

Check that `pg_up` is `1` in Prometheus, then look for `pg_stat_statements_*` metrics after
generating database traffic. The statement collector is opt-in because it adds overhead and a
series per query identity, and it returns only the top 50 statements — not a complete SQL
audit log. A missing series does not prove a query never ran; use the SQL statistics in
section 7 as well.

## 6. Useful Grafana panels (PromQL)

In Grafana, create a dashboard, add a visualization, choose Prometheus and paste a query. Use
time series for trends and tables for rankings, and mind the units. Run representative
traffic for several minutes — `rate(...[5m])` needs multiple samples — and compare the same
window across API, PostgreSQL and Kubernetes panels.

### API CPU usage in cores, by Pod

```promql
sum by (pod) (
  rate(container_cpu_usage_seconds_total{
    namespace="m3a", container="api", pod=~"m3a-api-.*"
  }[5m])
)
```

### API working-set memory in bytes, by Pod

```promql
sum by (pod) (
  container_memory_working_set_bytes{
    namespace="m3a", container="api", pod=~"m3a-api-.*"
  }
)
```

Repeat both with `container="postgres"` and `pod="m3a-db-0"` for the database. The API HPA
targets 40% of its `100m` CPU request — about `0.04` core per Pod, not 40% of the node.

### Requests per second, by route (requires section 3)

```promql
sum by (http_route) (
  rate(http_server_request_duration_seconds_count{
    namespace="m3a", service="m3a-api", http_route!="/metrics"
  }[5m])
)
```

### p95 request duration in seconds, by route

```promql
histogram_quantile(0.95,
  sum by (le, http_route) (
    rate(http_server_request_duration_seconds_bucket{
      namespace="m3a", service="m3a-api", http_route!="/metrics"
    }[5m])
  )
)
```

Aggregate buckets across Pods **before** computing the percentile; never average per-Pod p95
values. Low-traffic percentiles are noisy.

### HTTP 5xx responses per second, by route

```promql
sum by (http_route) (
  rate(http_server_request_duration_seconds_count{
    namespace="m3a", service="m3a-api",
    http_response_status_code=~"5.."
  }[5m])
)
```

The Locust suite deliberately triggers `422` business-rule rejections; filter on `4..` to see
them, but they are expected traffic, not errors.

OpenTelemetry metric and label names can vary with package versions and exporter settings.
If a query is empty, inspect `/metrics` or Prometheus's metric browser; these examples assume
the default translated names and classic histogram buckets. Routes are templates
(`/events/{eventId}`), not individual ids.

### Npgsql connections in use vs. idle, by Pod

```promql
sum by (pod, db_client_connection_state) (
  db_client_connection_count{namespace="m3a", service="m3a-api"}
)
```

### Top SQL queries by database execution time per second

```promql
topk(10,
  sum by (queryid) (
    rate(pg_stat_statements_seconds_total{datname="m3a_db", user="m3a_svc"}[5m])
  )
)
```

### Top SQL queries by average execution time in seconds

```promql
topk(10,
  sum by (queryid) (
    rate(pg_stat_statements_seconds_total{datname="m3a_db", user="m3a_svc"}[5m])
  )
  /
  (
    sum by (queryid) (
      rate(pg_stat_statements_calls_total{datname="m3a_db", user="m3a_svc"}[5m])
    ) > 0
  )
)
```

Rank by both: a fast query called constantly can cost more than a slow one called once.
Metrics carry `queryid`, not SQL text; map it back with section 7. Query ids are not stable
across PostgreSQL versions or databases.

## 7. Identify and investigate the actual SQL

Open a session as the local bootstrap superuser:

```bash
kubectl -n m3a exec -it m3a-db-0 -- psql -U postgres -d m3a_db
```

The service's most expensive statements. Times are **milliseconds**, cumulative since
statistics were last reset — not the Grafana window:

```sql
SELECT queryid, calls,
       round(total_exec_time::numeric, 2) AS total_ms,
       round(mean_exec_time::numeric, 2) AS mean_ms,
       round(max_exec_time::numeric, 2) AS max_ms,
       rows, shared_blks_hit, shared_blks_read,
       temp_blks_written, query
FROM pg_stat_statements
WHERE dbid = (SELECT oid FROM pg_database WHERE datname = current_database())
  AND userid = (SELECT oid FROM pg_roles WHERE rolname = 'm3a_svc')
ORDER BY total_exec_time DESC
LIMIT 20;
```

Lock waits and currently running queries:

```sql
SELECT pid, usename, now() - query_start AS elapsed,
       state, wait_event_type, wait_event, pg_blocking_pids(pid) AS blockers,
       query
FROM pg_stat_activity
WHERE datname = current_database()
  AND state <> 'idle' AND pid <> pg_backend_pid()
ORDER BY query_start;
```

Inspect a real repository query — for example the unpaginated `GET /tickets`
(`TicketRepository.GetAllAsync`). Creating an event issues `eventCapacity` ticket rows, so
this table grows much faster than `events` or `venues`:

```sql
EXPLAIN (ANALYZE, BUFFERS)
SELECT t."Id", t."EventId", t."Status" FROM tickets AS t;
```

`GET /events` (`EventRepository.GetAllAsync`) joins each event to its venue:

```sql
EXPLAIN (ANALYZE, BUFFERS)
SELECT e.*, v.* FROM events AS e INNER JOIN venues AS v ON e."VenueId" = v."Id";
```

Look for expensive sorts, sequential scans of large tables, inaccurate row estimates,
temporary disk spills and many buffer reads. A sequential scan is often right for small
tables or queries returning most rows — do not add an index just because one appears. No
index fixes an endpoint that returns an unbounded number of rows; that needs pagination.

**`EXPLAIN ANALYZE` executes the statement.** Start with plain `EXPLAIN` for unknown or
expensive queries, never run it on modifying statements against real data (transactions do
not undo every side effect), and test changes on disposable data. Do not reset shared query
statistics without coordinating with others; compare before/after samples or use Prometheus
rates instead.

## 8. A practical hotspot investigation

1. Generate representative traffic through the NodePort at `127.0.0.1:8080` with realistic
   data volumes — the [Locust suite](../loadtests/README.md) exercises every endpoint.
   Port-forward pins a single Pod and does not test Service load distribution. Do not
   benchmark only `/health`.
2. Find the slow route from p95 latency, throughput and errors.
3. Compare API/DB CPU and memory, HPA replica counts and Npgsql pool metrics. The API sets no
   `Maximum Pool Size`, so each process uses Npgsql's default of 100 connections; scaling from
   1 to 3 Pods triples the aggregate connection budget against PostgreSQL's
   `max_connections` (default 100).
4. Rank SQL by total and mean time. If latency rises without matching CPU, investigate locks.
5. Inspect the query plan. Change one thing (pagination, query shape, index, pool size), then
   rerun the same workload and measurement window.
6. If API CPU stays high while SQL is cheap, use `dotnet-trace`/`dotnet-counters` or a
   profiler. Tempo traces (section 3) show which request ran which queries; Prometheus metrics
   alone cannot provide stack traces or per-request correlation.

## Troubleshooting

- **`localhost:8080` answers with something else (e.g. Jenkins):** `localhost` resolves to
  IPv6 `::1` first, and another container runtime (Podman's `gvproxy`) may be listening on
  `*:8080`. The kind mappings are IPv4-only — use `127.0.0.1`. Check with
  `lsof -nP -iTCP:8080 -sTCP:LISTEN`.
- **Target missing:** `kubectl get servicemonitors -A`, the Service labels, and the
  ServiceMonitor's `namespaceSelector`. With `prometheus-values.yml` Prometheus selects
  ServiceMonitors regardless of the `release: monitoring` label.
- **API target down / 404:** deploy the instrumented image and check `/metrics` and the
  Service's named `http` port. `/health/ready` succeeding is not enough.
- **Trace datasource missing:** apply `tempo-datasource.yml` and inspect
  `kubectl -n monitoring logs deployment/monitoring-grafana -c grafana-sc-datasources`.
  The datasource is named `Tempo`, UID `tempo`.
- **Trace datasource connection error:** check Tempo readiness and the internal URL on port
  3200; `kubectl -n monitoring logs deployment/tempo`. A permissions error on `/var/tempo`
  means the volume is not writable by UID 10001.
- **Tempo connected, no traces:** check the API's `OTEL_*` environment
  (`kubectl -n m3a get deploy m3a-api -o yaml`), rebuild the image if needed, and send
  non-health requests. Check the Grafana time range and sampling. Service graphs and TraceQL
  metrics need a metrics-generator, which this setup does not enable.
- **PostgreSQL metrics absent:** `kubectl -n m3a logs deployment/postgres-exporter`; confirm
  `pg_up`, the Secret's credentials, the extension and `SHOW shared_preload_libraries`.
- **No SQL series yet:** generate query traffic and wait for scrapes. The collector limits its
  result set; direct SQL statistics are the fallback.
- **Kubernetes CPU metrics missing:** check the kubelet/cAdvisor targets in Prometheus. The
  kind control-plane targets are disabled in the values file on purpose.
- **Unexpected slowdown:** monitor the monitors. Scraping, tracing and collecting SQL
  statistics consume resources on the same machine.

## Teardown

```bash
kubectl delete -f kubernetes/monitoring/postgres-exporter.yml --ignore-not-found
kubectl -n m3a delete secret postgres-exporter --ignore-not-found
kubectl delete -f kubernetes/monitoring/api-servicemonitor.yml --ignore-not-found
kubectl delete -f kubernetes/monitoring/tempo-datasource.yml -f kubernetes/monitoring/tempo.yml --ignore-not-found
helm uninstall monitoring -n monitoring
```

Helm leaves the Prometheus Operator CRDs installed. `pg_stat_statements` stays enabled in the
database until you reset it or revert `shared_preload_libraries`.

Do not expose Grafana, Prometheus, Tempo, exporter ports or SQL text publicly. This guide does
not change HPA settings. The dashboard host ports in `kind.yaml` are localhost-only, and
Prometheus has no authentication — do not bind them to public interfaces without access
controls.
