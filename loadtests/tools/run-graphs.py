#!/usr/bin/env python3
"""Graphs and a per-step summary for a load-test run directory.

    python3 loadtests/tools/run-graphs.py loadtests/results/<run> [--prom http://127.0.0.1:9090]

Reads <run>/start_epoch, <run>/end_epoch and <run>/locust_stats_history.csv, queries Prometheus
over that window, and writes PNGs to <run>/graphs/ plus <run>/step-summary.csv. Needs matplotlib.
"""

from __future__ import annotations

import argparse
import csv
import json
import os
import urllib.parse
import urllib.request
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.dates as mdates  # noqa: E402
import matplotlib.pyplot as plt  # noqa: E402

API = 'namespace="m3a",pod=~"m3a-api-.*"'

# Limits drawn as reference lines; override for runs with a different configuration, e.g.
#   GRAPH_CPU_LIMIT=none GRAPH_MEM_LIMIT=1024 GRAPH_POOL_MAX=9 python3 run-graphs.py <run>
def _limit(name: str, default: str):
    v = os.getenv(name, default)
    return None if v.lower() in ("", "none") else float(v)

CPU_LIMIT = _limit("GRAPH_CPU_LIMIT", "0.1")
MEM_LIMIT = _limit("GRAPH_MEM_LIMIT", "512")
POOL_MAX = _limit("GRAPH_POOL_MAX", "100")
_cpu_t = f"limit {CPU_LIMIT:g}" if CPU_LIMIT else "no limit"
_mem_t = f"limit {MEM_LIMIT:g}" if MEM_LIMIT else "no limit"
_pool_t = f"pool max {POOL_MAX:g} / Pod" if POOL_MAX else "pool"
QUERIES = {
    "prometheus-01-api-cpu-cores": (
        f"API CPU (cores) per Pod — {_cpu_t}",
        f'sum by (pod) (rate(container_cpu_usage_seconds_total{{{API},container="api"}}[30s]))', CPU_LIMIT),
    "prometheus-02-api-cpu-throttled-ratio": (
        "API share of CFS periods throttled",
        f'sum by (pod) (rate(container_cpu_cfs_throttled_periods_total{{{API},container="api"}}[30s]))'
        f' / sum by (pod) (rate(container_cpu_cfs_periods_total{{{API},container="api"}}[30s]))', None),
    "prometheus-03-api-memory-mib": (
        f"API working set (MiB) — {_mem_t}",
        f'sum by (pod) (container_memory_working_set_bytes{{{API},container="api"}}) / 1048576', MEM_LIMIT),
    "prometheus-04-server-rps-by-status": (
        "Server-side requests/s by status code (all Pods)",
        'sum by (http_response_status_code) (rate(http_server_request_duration_seconds_count'
        '{namespace="m3a",http_route!~"/metrics|/health.*"}[30s]))', None),
    "prometheus-05-server-p95-latency": (
        "Server-side p95 latency (s) per Pod — excludes time queued before Kestrel",
        'histogram_quantile(0.95, sum by (pod, le) (rate(http_server_request_duration_seconds_bucket'
        '{namespace="m3a",http_route!~"/metrics|/health.*"}[30s])))', None),
    "prometheus-06-npgsql-connections": (
        f"Npgsql connections per Pod and state ({_pool_t}; Postgres max_connections 100)",
        'sum by (pod, db_client_connection_state) (db_client_connection_count{namespace="m3a"})', POOL_MAX),
    "prometheus-07-api-active-requests": (
        "In-flight requests per Pod (http_server_active_requests)",
        'sum by (pod) (http_server_active_requests{namespace="m3a"})', None),
    "prometheus-08-kestrel-connections": (
        "Kestrel active connections per Pod",
        'sum by (pod) (kestrel_active_connections{namespace="m3a"})', None),
    "prometheus-09-hpa-replicas": (
        "HPA current vs desired replicas",
        'label_replace(kube_horizontalpodautoscaler_status_current_replicas{namespace="m3a"}, "s", "current", "", "")'
        ' or label_replace(kube_horizontalpodautoscaler_status_desired_replicas{namespace="m3a"}, "s", "desired", "", "")',
        3),
    "prometheus-10-db-cpu-cores": (
        "PostgreSQL CPU (cores)",
        'sum(rate(container_cpu_usage_seconds_total{namespace="m3a",pod="m3a-db-0",container!=""}[30s]))', None),
    "prometheus-11-api-restarts": (
        "API container restarts (cumulative)",
        'sum by (pod) (kube_pod_container_status_restarts_total{namespace="m3a",container="api"})', None),
    "prometheus-12-threadpool-queue": (
        ".NET thread-pool queue length (work items waiting) per Pod",
        'sum by (pod) (rate(dotnet_thread_pool_queue_length_total{namespace="m3a"}[30s]))', None),
    "prometheus-14-traefik-cpu-memory": (
        "Traefik CPU (cores) per Pod — empty when Traefik is not in the path",
        'sum by (pod) (rate(container_cpu_usage_seconds_total{namespace="traefik",container!=""}[30s]))', None),
    "prometheus-13-node-cpu": (
        "Node CPU busy (cores of 4) — whole VM",
        'sum(rate(node_cpu_seconds_total{mode!="idle"}[30s]))', 4),
}


def prom_range(base: str, query: str, start: int, end: int, step: int = 5):
    qs = urllib.parse.urlencode({"query": query, "start": start, "end": end, "step": step})
    with urllib.request.urlopen(f"{base}/api/v1/query_range?{qs}", timeout=60) as resp:
        data = json.load(resp)
    if data.get("status") != "success":
        raise RuntimeError(data)
    return data["data"]["result"]


def label_of(metric: dict) -> str:
    parts = []
    for key in ("pod", "db_client_connection_state", "http_response_status_code", "s"):
        if key in metric:
            value = metric[key]
            parts.append(value.replace("m3a-api-", "").split("-")[-1] if key == "pod" else value)
    return " ".join(parts) or "value"


def step_boundaries(history: list[dict]) -> list[tuple[int, int, int]]:
    """(users, first_ts, last_ts) for each contiguous block of the target user count."""
    blocks: list[list[int]] = []
    for row in history:
        users, ts = int(row["User Count"]), int(row["Timestamp"])
        if blocks and blocks[-1][0] == users:
            blocks[-1][2] = ts
        else:
            blocks.append([users, ts, ts])
    return [tuple(b) for b in blocks]


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("run_dir", type=Path)
    ap.add_argument("--prom", default="http://127.0.0.1:9090")
    args = ap.parse_args()
    run: Path = args.run_dir
    out = run / "graphs"
    out.mkdir(exist_ok=True)
    start = int((run / "start_epoch").read_text())
    end = int((run / "end_epoch").read_text())

    # ---------------------------------------------------------------- Locust timeline
    with open(run / "locust_stats_history.csv") as f:
        history = [r for r in csv.DictReader(f) if r["Name"] == "Aggregated"]
    ts = [datetime.fromtimestamp(int(r["Timestamp"]), timezone.utc) for r in history]

    def col(name):
        return [float(r[name]) if r[name] not in ("", "N/A") else float("nan") for r in history]

    fig, axes = plt.subplots(4, 1, figsize=(12, 11), sharex=True)
    axes[0].plot(ts, col("User Count"), color="black")
    axes[0].set_ylabel("users")
    axes[1].plot(ts, col("Requests/s"), label="requests/s")
    axes[1].plot(ts, col("Failures/s"), label="failures/s", color="red")
    axes[1].set_ylabel("per second")
    axes[1].legend()
    total, fails = col("Total Request Count"), col("Total Failure Count")
    axes[2].plot(ts, [100 * f / t if t else 0 for f, t in zip(fails, total)], color="red")
    axes[2].set_ylabel("cumulative fail %")
    for p in ("50%", "95%", "99%"):
        axes[3].plot(ts, col(p), label=p)
    axes[3].set_yscale("log")
    axes[3].set_ylabel("response time ms (log)")
    axes[3].legend()
    axes[3].xaxis.set_major_formatter(mdates.DateFormatter("%H:%M"))
    fig.suptitle("Locust — stepped stress ramp (UTC)")
    fig.tight_layout()
    fig.savefig(out / "locust-01-timeline.png", dpi=110)
    plt.close(fig)

    # ---------------------------------------------------------------- per-step summary
    rows = []
    for users, t0, t1 in step_boundaries(history):
        if users == 0 or t1 - t0 < 10:  # skip the 1 s blocks Locust logs while stepping up
            continue
        block = [r for r in history if t0 <= int(r["Timestamp"]) <= t1]
        # Skip the first 20 s of a step (ramp + warm-up) when the step is long enough.
        steady = [r for r in block if int(r["Timestamp"]) >= t0 + 20] or block
        first, last = block[0], block[-1]
        reqs = float(last["Total Request Count"]) - float(first["Total Request Count"])
        fls = float(last["Total Failure Count"]) - float(first["Total Failure Count"])
        secs = max(t1 - t0, 1)

        def avg(name, rs=steady):
            vals = [float(r[name]) for r in rs if r[name] not in ("", "N/A")]
            return sum(vals) / len(vals) if vals else float("nan")

        rows.append({
            "users": users, "start_utc": datetime.fromtimestamp(t0, timezone.utc).strftime("%H:%M:%S"),
            "seconds": secs, "requests": int(reqs), "failures": int(fls),
            "fail_pct": round(100 * fls / reqs, 2) if reqs else 0.0,
            "req_per_s": round(reqs / secs, 1), "ok_req_per_s": round((reqs - fls) / secs, 1),
            "p50_ms": round(avg("50%")), "p95_ms": round(avg("95%")), "p99_ms": round(avg("99%")),
        })
    with open(run / "step-summary.csv", "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0]))
        w.writeheader()
        w.writerows(rows)
    for r in rows:
        print(r)

    # ---------------------------------------------------------------- Prometheus graphs
    summary: dict[str, dict[str, float]] = defaultdict(dict)
    for name, (title, query, ref) in QUERIES.items():
        try:
            series = prom_range(args.prom, query, start, end)
        except Exception as exc:  # keep going; one missing metric shouldn't lose the rest
            print(f"{name}: {exc}")
            continue
        fig, ax = plt.subplots(figsize=(12, 4.5))
        for s in sorted(series, key=lambda s: label_of(s["metric"])):
            xs = [datetime.fromtimestamp(float(t), timezone.utc) for t, _ in s["values"]]
            ys = [float(v) for _, v in s["values"]]
            lbl = label_of(s["metric"])
            ax.plot(xs, ys, label=lbl)
            finite = [y for y in ys if y == y]
            if finite:
                summary[name][lbl] = max(finite)
        if ref is not None:
            ax.axhline(ref, color="grey", linestyle="--", linewidth=1)
        ax.set_title(title)
        ax.xaxis.set_major_formatter(mdates.DateFormatter("%H:%M"))
        if series:
            ax.legend(fontsize=8, loc="upper left")
        fig.tight_layout()
        fig.savefig(out / f"{name}.png", dpi=110)
        plt.close(fig)
    (run / "prometheus-peaks.json").write_text(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
