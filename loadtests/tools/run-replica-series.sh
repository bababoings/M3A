#!/usr/bin/env bash
# Runs the reference load (16 users, 1 user/s, 5 min, server-side ticket filter) against the API at
# each replica count, once through the NodePort (direct) and once through Traefik.
#
#   loadtests/tools/run-replica-series.sh <prefix> <replicas...>
#   loadtests/tools/run-replica-series.sh 05 1 2 3 4   # -> 05-1rep-directa, 05-1rep-traefik, ...
#
# Replicas are pinned through the HPA (minReplicas = maxReplicas) so they cannot change mid-run.
set -euo pipefail
cd "$(dirname "$0")/../.."
prefix=${1:?usage: run-replica-series.sh <prefix> <replicas...>}
shift
export STRESS_SPAWN_RATE=1 TICKETS_FILTER=server
users=${USERS:-16}
seconds=${SECONDS_PER_RUN:-300}

for n in "$@"; do
  kubectl -n m3a patch hpa m3a-api --type=merge -p "{\"spec\":{\"minReplicas\":$n,\"maxReplicas\":$n}}" >/dev/null
  kubectl -n m3a scale deployment/m3a-api --replicas="$n" >/dev/null
  kubectl -n m3a rollout status deployment/m3a-api --timeout=300s >/dev/null
  echo "== $n replica(s)"
  loadtests/tools/run-scenario.sh "$prefix-${n}rep-directa" "$users" "$seconds" \
    > "loadtests/results/$prefix-${n}rep-directa.log" 2>&1
  grep -E "^\| $users \||Journeys" "loadtests/results/$prefix-${n}rep-directa/summary.md" || true
  HOST=http://127.0.0.1:8081 STRESS_HOST_HEADER=m3a.local \
    loadtests/tools/run-scenario.sh "$prefix-${n}rep-traefik" "$users" "$seconds" \
    > "loadtests/results/$prefix-${n}rep-traefik.log" 2>&1
  grep -E "^\| $users \||Journeys" "loadtests/results/$prefix-${n}rep-traefik/summary.md" || true
done
