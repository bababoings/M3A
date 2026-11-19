#!/usr/bin/env bash
# Samples the kind node, the HPA and the m3a Pods every INTERVAL seconds until killed.
#   loadtests/tools/sample-node.sh <out-file> [interval-seconds]
# Each sample: UTC time, node load average, available memory, HPA state, then one line per m3a
# Pod (ready/status/restarts, plus CPU/memory from Metrics Server) and PostgreSQL connections.
set -u
out=${1:?usage: sample-node.sh <out-file> [interval]}
interval=${2:-5}
node=${KIND_NODE:-kind-control-plane}

while true; do
  {
    load=$(docker exec "$node" cut -d' ' -f1 /proc/loadavg 2>/dev/null)
    avail=$(docker exec "$node" awk '/MemAvailable/ {print int($2/1024)}' /proc/meminfo 2>/dev/null)
    echo "$(date -u +%H:%M:%S) load=${load:-?} avail_mb=${avail:-?}"
    kubectl -n m3a get hpa m3a-api --no-headers \
      -o custom-columns='T:.status.currentMetrics[0].resource.current.averageUtilization,R:.status.currentReplicas,D:.status.desiredReplicas' \
      --request-timeout=4s 2>/dev/null | awk '{print "   hpa cpu=" $1 "% replicas=" $2 " desired=" $3}'
    kubectl -n m3a get pods --no-headers --request-timeout=4s 2>/dev/null \
      | awk '{print "   pod " $1 " ready=" $2 " " $3 " restarts=" $4}'
    kubectl -n m3a top pods --no-headers --request-timeout=4s 2>/dev/null \
      | awk '{print "   top " $1 " cpu=" $2 " mem=" $3}'
    kubectl -n traefik top pods --no-headers --request-timeout=4s 2>/dev/null \
      | awk '{print "   top traefik/" $1 " cpu=" $2 " mem=" $3}'
    conns=$(kubectl -n m3a exec m3a-db-0 --request-timeout=4s -- env PGPASSWORD=password \
      psql -U m3a_svc -d m3a_db -h 127.0.0.1 -tAc \
      "select count(*) from pg_stat_activity where datname='m3a_db'" 2>/dev/null)
    echo "   pg connections=${conns:-?}"
  } >>"$out"
  sleep "$interval"
done
