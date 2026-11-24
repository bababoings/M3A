#!/usr/bin/env python3
"""Markdown summary of one run directory written by run-scenario.sh.

    python3 loadtests/tools/summarize.py loadtests/results/<run> [--prom http://127.0.0.1:9090]

Prints:
  * per-step table (users, req/s, successful req/s, failure %, p50/p95/p99, API and DB CPU) —
    from step-summary.csv plus Prometheus averages over each step's steady window;
  * N60: the first step after the peak whose successful req/s is <= 60% of the peak (or, when
    throughput plateaus instead of falling, whose successful req/s per user is <= 60% of the
    first step's) — only meaningful for stepped ramps;
  * per-endpoint table from locust_stats.csv (whole run).
"""

from __future__ import annotations

import argparse
import csv
import json
import urllib.parse
import urllib.request
from pathlib import Path

API_CPU = 'sum(rate(container_cpu_usage_seconds_total{namespace="m3a",pod=~"m3a-api-.*",container="api"}[30s]))'
API_THR = ('sum(rate(container_cpu_cfs_throttled_periods_total{namespace="m3a",pod=~"m3a-api-.*",container="api"}[30s]))'
           ' / sum(rate(container_cpu_cfs_periods_total{namespace="m3a",pod=~"m3a-api-.*",container="api"}[30s]))')
DB_CPU = 'sum(rate(container_cpu_usage_seconds_total{namespace="m3a",pod="m3a-db-0",container!=""}[30s]))'
TRAEFIK_CPU = 'sum(rate(container_cpu_usage_seconds_total{namespace="traefik",container!=""}[30s]))'
API_MEM = 'max(container_memory_working_set_bytes{namespace="m3a",pod=~"m3a-api-.*",container="api"}) / 1048576'


def prom_avg(base: str, query: str, start: int, end: int) -> float | None:
    qs = urllib.parse.urlencode({"query": query, "start": start, "end": end, "step": 5})
    try:
        with urllib.request.urlopen(f"{base}/api/v1/query_range?{qs}", timeout=30) as resp:
            result = json.load(resp)["data"]["result"]
    except Exception:  # noqa: BLE001 — Prometheus is optional
        return None
    vals = [float(v) for s in result for _, v in s["values"] if v not in ("NaN", "+Inf")]
    return sum(vals) / len(vals) if vals else None


def fmt(v: float | None, spec: str) -> str:
    return "—" if v is None else format(v, spec)


def steps_from_history(run: Path) -> list[tuple[int, int, int]]:
    blocks: list[list[int]] = []
    with open(run / "locust_stats_history.csv") as f:
        for row in csv.DictReader(f):
            if row["Name"] != "Aggregated":
                continue
            users, ts = int(row["User Count"]), int(row["Timestamp"])
            if blocks and blocks[-1][0] == users:
                blocks[-1][2] = ts
            else:
                blocks.append([users, ts, ts])
    return [tuple(b) for b in blocks if b[0] and b[2] - b[1] >= 10]


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("run_dir", type=Path)
    ap.add_argument("--prom", default="http://127.0.0.1:9090")
    args = ap.parse_args()
    run = args.run_dir

    print(f"# {run.name}\n")
    windows = {u: (t0, t1) for u, t0, t1 in steps_from_history(run)}

    steps = []
    if (run / "step-summary.csv").exists():
        with open(run / "step-summary.csv") as f:
            steps = list(csv.DictReader(f))

    if steps:
        print("## Por escalón\n")
        print("| usuarios | req/s | req/s OK | fallos % | p50 ms | p95 ms | p99 ms | CPU API (cores) | throttling API | CPU BD (cores) | CPU Traefik (cores) | mem API MiB |")
        print("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|")
        for s in steps:
            u = int(s["users"])
            t0, t1 = windows.get(u, (None, None))
            cpu = thr = db = mem = tr = None
            if t0:
                a = t0 + 20 if t1 - t0 > 40 else t0
                cpu, thr = prom_avg(args.prom, API_CPU, a, t1), prom_avg(args.prom, API_THR, a, t1)
                db, mem = prom_avg(args.prom, DB_CPU, a, t1), prom_avg(args.prom, API_MEM, a, t1)
                tr = prom_avg(args.prom, TRAEFIK_CPU, a, t1)
            s["cpu"] = cpu
            print(f"| {u} | {s['req_per_s']} | {s['ok_req_per_s']} | {s['fail_pct']} | {s['p50_ms']} | "
                  f"{s['p95_ms']} | {s['p99_ms']} | {fmt(cpu, '.3f')} | {fmt(thr and thr * 100, '.0f')}% | "
                  f"{fmt(db, '.3f')} | {fmt(tr, '.3f')} | {fmt(mem, '.0f')} |")

        if len(steps) > 1:
            ok = [float(s["ok_req_per_s"]) for s in steps]
            users = [int(s["users"]) for s in steps]
            peak_i = max(range(len(ok)), key=ok.__getitem__)
            n60 = next((users[i] for i in range(peak_i + 1, len(ok)) if ok[i] <= 0.6 * ok[peak_i]), None)
            print(f"\nPico: {ok[peak_i]} req/s OK con {users[peak_i]} usuarios; 60% del pico = {0.6 * ok[peak_i]:.1f} req/s.")
            if n60:
                print(f"**N60 (req/s OK <= 60% del pico) = {n60} usuarios** → 80% = {0.8 * n60:g} usuarios.")
            else:
                eff0 = ok[0] / users[0]
                n60e = next((users[i] for i in range(len(ok)) if ok[i] / users[i] <= 0.6 * eff0), None)
                print(f"El throughput no cayó al 60% del pico dentro de la rampa. Criterio de respaldo "
                      f"(req/s OK por usuario <= 60% de {eff0:.2f}): N60 = {n60e} usuarios.")
        print()

    print("## Por endpoint (toda la corrida)\n")
    print("| endpoint | tipo | peticiones | fallos | mediana ms | promedio ms | p95 ms | p99 ms | req/s |")
    print("|---|---|---:|---:|---:|---:|---:|---:|---:|")
    with open(run / "locust_stats.csv") as f:
        rows = list(csv.DictReader(f))
    for r in sorted(rows, key=lambda r: (r["Name"] == "Aggregated", -float(r["Average Response Time"] or 0))):
        print(f"| {r['Name']} | {r['Type']} | {r['Request Count']} | {r['Failure Count']} | "
              f"{r['Median Response Time']} | {float(r['Average Response Time']):.0f} | {r['95%']} | "
              f"{r['99%']} | {float(r['Requests/s']):.2f} |")

    # The expected-422 delete is the journey's last step: each success is one completed journey.
    done = next((r for r in rows if r["Name"] == "/events/{id} [has sold tickets]"), None)
    agg = next(r for r in rows if r["Name"] == "Aggregated")
    secs = max(int((run / "end_epoch").read_text()) - int((run / "start_epoch").read_text()), 1)
    ok = int(done["Request Count"]) - int(done["Failure Count"]) if done else 0
    if True:
        print(f"\n**Journeys completados:** {ok} ({60 * ok / secs:.1f}/min); "
              f"peticiones OK totales: {int(agg['Request Count']) - int(agg['Failure Count'])}.")

    fails = run / "locust_failures.csv"
    if fails.exists():
        with open(fails) as f:
            frows = sorted(csv.DictReader(f), key=lambda r: -int(r["Occurrences"]))[:8]
        if frows:
            print("\n## Fallos más frecuentes\n")
            print("| endpoint | ocurrencias | error |")
            print("|---|---:|---|")
            for r in frows:
                err = r["Error"].replace("|", "/").replace("\n", " ")[:120]
                print(f"| {r['Method']} {r['Name']} | {r['Occurrences']} | {err} |")


if __name__ == "__main__":
    main()
