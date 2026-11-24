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

## Seed data

`seed.py` fills the database through the API (standard library only) so tests run against
realistic table sizes: by default 100 venues, 300 events and ~50k tickets, ~35% purchased and a
fifth of those redeemed. It takes about 3 minutes on kind and refuses to run twice unless you
pass `--append`. See `--help` for sizes and `--host` for a local `dotnet run`.

```sh
python3 loadtests/seed.py                                # http://127.0.0.1:8080
```

`GET /tickets` has no paging, and every journey calls it, so the seed size directly sets the
size of that response (~5.5 MB with the defaults).

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

## Stress / breaking-point run

`stress.py` runs the same journey with a stepped ramp (10 → 400 users, 90 s per step), 0.25–0.75 s
think time and a 10 s request timeout, to push past what the deployment can handle. Tunable via
`STRESS_STEPS`, `STRESS_STEP_SECONDS`, `STRESS_SPAWN_RATE`, `STRESS_TIMEOUT`.

```sh
D=loadtests/results/run-$(date +%F)-stress; mkdir -p $D
loadtests/tools/sample-node.sh $D/node-samples.txt 5 & SAMPLER=$!
date +%s > $D/start_epoch
.venv-loadtests/bin/locust -f loadtests/stress.py StressJourneyUser --host http://127.0.0.1:8080 \
  --headless --processes 4 --stop-timeout 30s --csv $D/locust --csv-full-history \
  --html $D/locust-report.html | tee $D/locust-console.txt
date +%s > $D/end_epoch; kill $SAMPLER
python3 loadtests/tools/run-graphs.py $D   # graphs/ + step-summary.csv (needs matplotlib)
```

The user class name (`StressJourneyUser`) is required. Failed journeys leave data behind, so run the
cleanup queries below afterwards. See `results/run-2026-11-18-stress/REPORT.md` for the first run.

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
