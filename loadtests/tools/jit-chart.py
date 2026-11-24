#!/usr/bin/env python3
"""JIT warm-up chart for the report: JIT compile time and served req/s over the run, before
(phase 4b, normal image) and after (phase 5, ReadyToRun + DOTNET_TieredPGO=0).

    .venv-loadtests/bin/python loadtests/tools/jit-chart.py

Queries Prometheus (127.0.0.1:9090) over each run's start/end epoch and writes
docs/stress-test/img/comparativa-jit.png.
"""

from __future__ import annotations

import json
import urllib.parse
import urllib.request
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
RESULTS = ROOT / "loadtests/results"
PROM = "http://127.0.0.1:9090/api/v1/query_range"
RUNS = [
    ("04b-filtro-directa", "fase 4b directa (imagen normal)", "#2f6db5", ":"),
    ("04b-filtro-traefik", "fase 4b Traefik (imagen normal)", "#2e9a6b", ":"),
    ("05-1rep-directa", "fase 5 directa (ReadyToRun)", "#2f6db5", "-"),
    ("05-1rep-traefik", "fase 5 Traefik (ReadyToRun)", "#2e9a6b", "-"),
]
JIT = 'sum(rate(dotnet_jit_compilation_time_seconds_total{namespace="m3a"}[30s]))'
RPS = 'sum(rate(http_server_request_duration_seconds_count{namespace="m3a",http_route!~"/metrics|/health.*"}[30s]))'


def series(expr: str, start: int, end: int) -> tuple[list[float], list[float]]:
    qs = urllib.parse.urlencode({"query": expr, "start": start, "end": end, "step": 10})
    res = json.load(urllib.request.urlopen(f"{PROM}?{qs}"))["data"]["result"]
    if not res:
        return [], []
    vals = res[0]["values"]
    return [(float(t) - start) for t, _ in vals], [float(v) for _, v in vals]


def main() -> None:
    fig, (a, b) = plt.subplots(1, 2, figsize=(14, 4.6), constrained_layout=True)
    for run, label, color, ls in RUNS:
        d = RESULTS / run
        start, end = int((d / "start_epoch").read_text()), int((d / "end_epoch").read_text())
        x, y = series(JIT, start, end)
        a.plot(x, y, ls, color=color, label=label)
        x, y = series(RPS, start, end)
        b.plot(x, y, ls, color=color, label=label)
    a.set_title("Tiempo de compilación JIT (s de JIT por s) — 1 réplica, 100m")
    a.set_ylabel("s/s")
    b.set_title("Peticiones atendidas por la API (req/s, servidor)")
    b.set_ylabel("req/s")
    for ax in (a, b):
        ax.set_xlabel("segundos desde el inicio de la corrida")
        ax.grid(alpha=0.3)
        ax.spines[["top", "right"]].set_visible(False)
    b.legend(fontsize=8)
    out = ROOT / "docs/stress-test/img/comparativa-jit.png"
    fig.savefig(out, dpi=130)
    print(f"wrote {out}")


if __name__ == "__main__":
    main()
