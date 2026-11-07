# Load tests (Locust)

`locustfile.py` runs one **end-to-end journey per iteration** that calls all 18 endpoints of the
API — health, venues, events, tickets and the purchase/redeem transitions — including the
business-rule rejections (sold out, redeem before purchase, delete an event with sold tickets),
which are counted as successes when they return the expected `422`. Each journey deletes what it
created, so the database stays the same size however long the test runs.

Each journey also ends by closing its HTTP connection. Kubernetes Services balance per
connection, not per request, so with keep-alive every user would stay on the Pod it first
reached and Pods added by the HPA mid-run would receive almost no traffic.

## Setup

```sh
python3 -m venv .venv-loadtests
.venv-loadtests/bin/pip install -r loadtests/requirements.txt
```

## Run

Against the kind deployment (`docs/kubernetes-deployment.md`), from the repository root:

```sh
# Web UI at http://localhost:8089 — choose users / spawn rate there
.venv-loadtests/bin/locust -f loadtests/locustfile.py --host http://127.0.0.1:8080

# Headless: 10 users, 5 spawned per second, 2 minutes
.venv-loadtests/bin/locust -f loadtests/locustfile.py --host http://127.0.0.1:8080 \
  --headless -u 10 -r 5 -t 2m --stop-timeout 15s --csv loadtests/results/run
```

**Always pass `--stop-timeout` on timed runs.** Without it Locust kills journeys mid-way when the
time is up and their venues/events/tickets are left in the database. Running `dotnet run`
locally instead? Use `--host http://localhost:5080`.

Watch the API scale while it runs:

```sh
kubectl -n m3a get hpa m3a-api --watch
kubectl top pods -n m3a
```

## Reading the results

Requests are grouped by route template (`/tickets/{id}`), and expected rejections have their own
rows, e.g. `POST /tickets [sold out]`. A row named after a sentence (type `CHECK`) is a failed
assertion on a response body, e.g. a ticket that did not become `Purchased`.

`GET /tickets` has no filter and returns every ticket in the system, so it is the endpoint whose
latency grows with data — start from a clean database for comparable runs.

Reference run (kind, 20 users, 60 s): 14,296 requests, 0 failures, ~240 req/s, median 2 ms,
p99 7 ms; the HPA scaled the API to 3 replicas.

## Cleaning up after an interrupted run

```sh
q() { kubectl -n m3a exec m3a-db-0 -- env PGPASSWORD=password \
        psql -U m3a_svc -d m3a_db -h 127.0.0.1 -tAc "$1"; }
q "delete from tickets where \"EventId\" in (select \"Id\" from events where \"Name\" like 'Load Test Event %')"
q "delete from events  where \"Name\" like 'Load Test Event %'"
q "delete from venues  where \"Location\" like 'Load Test Arena %'"
```
