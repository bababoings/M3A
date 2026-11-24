#!/usr/bin/env bash
# One measured load-test scenario against the kind deployment, with everything the report needs.
#
#   loadtests/tools/run-scenario.sh <name> <users[,users...]> <seconds-per-step>
#
#   loadtests/tools/run-scenario.sh 01-ramp-sin-indices 1,2,3,4,6,8,10,15,20 90   # stepped ramp
#   loadtests/tools/run-scenario.sh 02-base-sin-indices 5 300                      # constant load
#
# Through Traefik instead of the API NodePort (relay container m3a-traefik-relay on host 8081):
#   HOST=http://127.0.0.1:8081 STRESS_HOST_HEADER=m3a.local loadtests/tools/run-scenario.sh ...
# Server-side ticket filter (image with GET /tickets?eventId=): TICKETS_FILTER=server
#
# Writes loadtests/results/<name>/:
#   config.txt               replicas, Pod resources, image, DB indexes at the start of the run
#   locust_*.csv, *.html     Locust stats (per endpoint), history, failures, HTML report
#   node-samples.txt         node load, Pods' CPU/memory, PG connections every 5 s
#   pg-stat-statements.txt   top SQL by total time during this run (stats reset at the start)
#   step-summary.csv, graphs/, prometheus-peaks.json   from run-graphs.py
#   summary.md               per-endpoint table + per-step table (summarize.py)
#   capturas/                Grafana + Prometheus screenshots of the run (capture-dashboards.py)
# and finally deletes rows left by failed journeys, so the next run starts from the same data.
set -euo pipefail
cd "$(dirname "$0")/../.."

name=${1:?usage: run-scenario.sh <name> <users[,users...]> <seconds-per-step>}
steps=${2:?users per step}
step_seconds=${3:?seconds per step}
host=${HOST:-http://127.0.0.1:8080}
D=loadtests/results/$name
mkdir -p "$D"

psql() { kubectl -n m3a exec m3a-db-0 -- psql -U postgres -d m3a_db "$@"; }

{
  echo "# $name — $(date -u +%FT%TZ)"
  echo "steps=$steps step_seconds=$step_seconds host=$host spawn_rate=${STRESS_SPAWN_RATE:-10}/s"
  echo "path=$([ -n "${STRESS_HOST_HEADER:-}" ] && echo "traefik (Host: $STRESS_HOST_HEADER)" || echo direct-nodeport) tickets_filter=${TICKETS_FILTER:-client}"
  kubectl -n m3a get deploy m3a-api -o jsonpath='replicas={.spec.replicas} image={.spec.template.spec.containers[0].image} resources={.spec.template.spec.containers[0].resources} env={.spec.template.spec.containers[0].env}{"\n"}'
  kubectl -n m3a get hpa m3a-api -o jsonpath='hpa min={.spec.minReplicas} max={.spec.maxReplicas}{"\n"}'
  kubectl -n m3a get pods -o wide
  echo "-- indexes"
  psql -tAc "select indexname || ': ' || indexdef from pg_indexes where schemaname='public' order by 1"
  echo "-- row counts"
  psql -tAc "select 'venues', count(*) from venues union all select 'events', count(*) from events union all select 'tickets', count(*) from tickets"
} > "$D/config.txt"
cat "$D/config.txt"

# Same starting state for every run: fresh API Pods (no leftover OOM/GC state, cold JIT), then
# a short warm-up so the first measured seconds are not dominated by JIT compilation.
kubectl -n m3a rollout restart deployment/m3a-api >/dev/null
kubectl -n m3a rollout status deployment/m3a-api --timeout=300s
for _ in $(seq 1 20); do
  for path in /health/ready /venues /events; do
    curl -s -o /dev/null -H "Host: ${STRESS_HOST_HEADER:-127.0.0.1}" "$host$path" || true
  done
done
sleep 10

psql -tAc "select pg_stat_statements_reset()" >/dev/null

loadtests/tools/sample-node.sh "$D/node-samples.txt" 5 &
SAMPLER=$!
trap 'kill $SAMPLER 2>/dev/null || true' EXIT

date +%s > "$D/start_epoch"
STRESS_STEPS=$steps STRESS_STEP_SECONDS=$step_seconds \
  .venv-loadtests/bin/locust -f loadtests/stress.py StressJourneyUser --host "$host" \
  --headless --processes 4 --stop-timeout 30s --only-summary \
  --csv "$D/locust" --csv-full-history --html "$D/locust-report.html" \
  > "$D/locust-console.txt" 2>&1 || true   # Locust exits 1 whenever any request failed
date +%s > "$D/end_epoch"
kill $SAMPLER 2>/dev/null || true

# Top statements by total execution time while the test ran.
psql -c "select calls, round(total_exec_time::numeric,0) as total_ms,
                round(mean_exec_time::numeric,2) as mean_ms, round(max_exec_time::numeric,1) as max_ms,
                rows, shared_blks_hit + shared_blks_read as blocks,
                left(regexp_replace(query, '\s+', ' ', 'g'), 220) as query
         from pg_stat_statements
         where dbid = (select oid from pg_database where datname='m3a_db')
         order by total_exec_time desc limit 20" > "$D/pg-stat-statements.txt"

sleep 35   # let Prometheus scrape the tail of the run before querying it
.venv-loadtests/bin/python loadtests/tools/run-graphs.py "$D" > "$D/run-graphs.txt" 2>&1 || cat "$D/run-graphs.txt"
.venv-loadtests/bin/python loadtests/tools/summarize.py "$D" > "$D/summary.md"
cat "$D/summary.md"
.venv-loadtests/bin/python loadtests/tools/capture-dashboards.py "$D" > "$D/capturas.txt" 2>&1 \
  || echo "captures failed, see $D/capturas.txt"

# Rows left behind by journeys that failed mid-way (same queries as loadtests/README.md).
psql -tAc "delete from tickets where \"EventId\" in (select \"Id\" from events where \"Name\" like 'Load Test Event %')" >/dev/null
psql -tAc "delete from events where \"Name\" like 'Load Test Event %'" >/dev/null
psql -tAc "delete from venues where \"Location\" like 'Load Test Arena %'" >/dev/null
psql -tAc "vacuum analyze" >/dev/null
echo "results in $D"
