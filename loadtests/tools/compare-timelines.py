#!/usr/bin/env python3
"""Time-aligned comparison figures for stress runs that used the same stepped ramp.

    python3 loadtests/tools/compare-timelines.py <out-dir> <label>=<run-dir> [...] [--prom URL]

Reads each run's start_epoch/end_epoch and locust_stats_history.csv, queries Prometheus over each
run's own window, and plots everything against *seconds since that run started*, so identical
ramps line up. Writes four PNGs:

  stress-timeline-load.png         successful req/s, failed req/s, p95 latency (runs overlaid)
  stress-timeline-pod-share.png    each Pod's share of API requests, one panel per run
  stress-timeline-db-connections.png  Npgsql pool slots in use per Pod + Postgres' actual total vs 100
  stress-timeline-cpu.png          node CPU (of 4 cores) and total API CPU (cap 0.3 cores)

Colour: runs use categorical slots 1-3 (as compare-runs.py); Pods use slots 4, 5, 7 plus distinct
line styles and direct labels. Both palettes pass the dataviz skill's validate_palette.js (light).
"""

from __future__ import annotations

import argparse
import csv
import json
import urllib.parse
import urllib.request
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

RUN_COLORS = ["#2a78d6", "#eb6834", "#1baf7a"]
RUN_MARKERS = ["o", "s", "D"]
POD_COLORS = ["#eda100", "#e87ba4", "#6250d6"]
POD_STYLES = ["-", "--", ":"]
SURFACE, INK, INK2, GRID, NEUTRAL = "#fcfcfb", "#0b0b0b", "#52514e", "#e4e3df", "#52514e"
STEP_SECONDS = 90
STEPS = [10, 25, 50, 100, 200, 300, 400]

API_ROUTES = 'namespace="m3a",http_route!~"/metrics|/health.*"'


def prom(base: str, query: str, start: int, end: int, step: int = 15) -> list[dict]:
    qs = urllib.parse.urlencode({"query": query, "start": start, "end": end, "step": step})
    with urllib.request.urlopen(f"{base}/api/v1/query_range?{qs}", timeout=60) as resp:
        data = json.load(resp)
    return data["data"]["result"]


def series(result: list[dict], start: int) -> dict[str, tuple[list[float], list[float]]]:
    """{pod-or-'value': (seconds since start, values)}"""
    out = {}
    for s in result:
        key = s["metric"].get("pod", "value")
        out[key] = ([float(t) - start for t, _ in s["values"]], [float(v) for _, v in s["values"]])
    return out


def with_gaps(xs: list[float], ys: list[float], max_gap: float = 40) -> tuple[list[float], list[float]]:
    """Insert a break (NaN) wherever samples are missing, so a line is not drawn across the gap."""
    gx, gy = [], []
    for i, (x, y) in enumerate(zip(xs, ys)):
        if i and x - xs[i - 1] > max_gap:
            gx.append((x + xs[i - 1]) / 2)
            gy.append(float("nan"))
        gx.append(x)
        gy.append(y)
    return gx, gy


def pods_in_order(per_pod: dict) -> list[str]:
    """API Pods ordered by when they first appear in the run: the long-lived (warm) Pod first."""
    api = {k: v for k, v in per_pod.items() if k.startswith("m3a-api-")}
    return sorted(api, key=lambda k: api[k][0][0] if api[k][0] else 1e9)


def style(ax, title: str, ylabel: str | None = None) -> None:
    ax.set_facecolor(SURFACE)
    ax.set_title(title, loc="left", fontsize=10.5, color=INK, pad=18)
    if ylabel:
        ax.set_ylabel(ylabel, color=INK2)
    ax.grid(axis="y", color=GRID, linewidth=1)
    ax.set_axisbelow(True)
    for side in ("top", "right"):
        ax.spines[side].set_visible(False)
    for side in ("left", "bottom"):
        ax.spines[side].set_color(GRID)
    ax.tick_params(length=0, colors=INK2)
    ax.set_xlim(0, STEP_SECONDS * len(STEPS) + 15)
    ax.set_xticks(range(0, STEP_SECONDS * len(STEPS) + 1, 90),
                  [f"{t // 60}:{t % 60:02d}" for t in range(0, STEP_SECONDS * len(STEPS) + 1, 90)])


def step_marks(ax) -> None:
    """Hairlines at each step boundary, user count written above the plot."""
    for i, users in enumerate(STEPS):
        x = i * STEP_SECONDS
        if i:
            ax.axvline(x, color=GRID, linewidth=1, zorder=0)
        ax.text(x + STEP_SECONDS / 2, 1.01, f"{users}u", transform=ax.get_xaxis_transform(),
                ha="center", va="bottom", fontsize=8, color=INK2)


def save(fig, path: Path) -> None:
    fig.savefig(path, dpi=130, facecolor=SURFACE)
    plt.close(fig)
    print(path)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("out_dir", type=Path)
    ap.add_argument("runs", nargs="+")
    ap.add_argument("--prom", default="http://127.0.0.1:9090")
    args = ap.parse_args()
    out: Path = args.out_dir
    out.mkdir(parents=True, exist_ok=True)
    plt.rcParams.update({"font.family": "sans-serif", "font.size": 9.5, "text.color": INK})

    runs = []
    for arg in args.runs:
        label, path = arg.split("=", 1)
        d = Path(path)
        start, end = int((d / "start_epoch").read_text()), int((d / "end_epoch").read_text())
        with open(d / "locust_stats_history.csv") as f:
            hist = [r for r in csv.DictReader(f) if r["Name"] == "Aggregated"]
        runs.append({"label": label, "dir": d, "start": start, "end": end, "hist": hist})

    # ------------------------------------------------------------- 1. load over time (Locust)
    fig, axes = plt.subplots(3, 1, figsize=(13, 10), sharex=True, facecolor=SURFACE)
    panels = [("ok", "Successful requests / s", None), ("Failures/s", "Failed requests / s", None),
              ("95%", "p95 response time (ms, log scale)", "log")]
    for ax, (key, title, scale) in zip(axes, panels):
        style(ax, title)
        step_marks(ax)
        for i, run in enumerate(runs):
            xs = [int(r["Timestamp"]) - run["start"] for r in run["hist"]]
            if key == "ok":
                ys = [float(r["Requests/s"]) - float(r["Failures/s"]) for r in run["hist"]]
            else:
                ys = [float(r[key]) if r[key] not in ("", "N/A") else float("nan") for r in run["hist"]]
            ax.plot(xs, ys, color=RUN_COLORS[i], linewidth=1.6, solid_joinstyle="round",
                    label=run["label"], marker=RUN_MARKERS[i], markevery=36, markersize=6,
                    markeredgecolor=SURFACE, markeredgewidth=1.5)
        if scale:
            ax.set_yscale("log")
    axes[-1].set_xlabel("time since run start (min:s) — every run used the same 10 → 400-user ramp", color=INK2)
    fig.legend(*axes[0].get_legend_handles_labels(), loc="upper center", ncol=len(runs), frameon=False,
               bbox_to_anchor=(0.5, 1.0), fontsize=10)
    fig.tight_layout(rect=(0, 0, 1, 0.96))
    save(fig, out / "stress-timeline-load.png")

    # ------------------------------------------------------------- 2. per-Pod request share
    share_q = (f"sum by (pod) (rate(http_server_request_duration_seconds_count{{{API_ROUTES}}}[1m]))"
               f" / scalar(sum(rate(http_server_request_duration_seconds_count{{{API_ROUTES}}}[1m])))")
    fig, axes = plt.subplots(1, len(runs), figsize=(15, 4.6), sharey=True, facecolor=SURFACE)
    for ax, run in zip(axes, runs):
        style(ax, run["label"])
        step_marks(ax)
        per_pod = series(prom(args.prom, share_q, run["start"], run["end"]), run["start"])
        ax.axhline(100 / 3, color=INK2, linewidth=1, linestyle=(0, (1, 2)), zorder=1)
        ax.text(5, 100 / 3 + 1.5, "even split (33%)", fontsize=7.5, color=INK2)
        for j, pod in enumerate(pods_in_order(per_pod)):
            xs, ys = per_pod[pod]
            name = f"Pod {j + 1}" + (" (warm)" if j == 0 else "") + f" …{pod[-5:]}"
            ax.plot(xs, [100 * y for y in ys], color=POD_COLORS[j], linestyle=POD_STYLES[j],
                    linewidth=2, label=name)
        ax.set_ylim(0, 100)
        ax.legend(fontsize=7.5, frameon=False, loc="upper right")
        ax.set_xlabel("min:s since start", color=INK2)
    axes[0].set_ylabel("share of API requests (%)", color=INK2)
    fig.suptitle("Each Pod's share of API requests (1-min rate). Round-robin keeps a struggling Pod at 33%; "
                 "kube-proxy and p2c move traffic away", x=0.01, ha="left", fontsize=11, color=INK)
    fig.tight_layout(rect=(0, 0, 1, 0.93))
    save(fig, out / "stress-timeline-pod-share.png")

    # ------------------------------------------------------------- 3. DB connections
    # Npgsql's "used" count is pool slots taken, which includes connection attempts that Postgres
    # refused, so the per-Pod values can add up to more than the server's limit. The Postgres total
    # (pg_stat_activity via postgres-exporter) is the real server-side count; it has gaps where the
    # exporter itself was refused a connection.
    used_q = 'sum by (pod) (db_client_connection_count{namespace="m3a",db_client_connection_state="used"})'
    total_q = 'sum(pg_stat_activity_count{datname="m3a_db"})'
    fig, axes = plt.subplots(1, len(runs), figsize=(15, 5), sharey=True, facecolor=SURFACE)
    for ax, run in zip(axes, runs):
        style(ax, run["label"])
        step_marks(ax)
        per_pod = series(prom(args.prom, used_q, run["start"], run["end"]), run["start"])
        total = series(prom(args.prom, total_q, run["start"], run["end"]), run["start"])
        ax.axhline(100, color=INK, linewidth=1, zorder=1)
        ax.text(STEP_SECONDS * len(STEPS) + 10, 96, "Postgres max_connections = 100", fontsize=7.5,
                color=INK, ha="right", va="top",
                bbox=dict(boxstyle="round,pad=0.15", facecolor=SURFACE, edgecolor="none"))
        for j, pod in enumerate(pods_in_order(per_pod)):
            xs, ys = with_gaps(*per_pod[pod])
            name = f"Pod {j + 1}" + (" (warm)" if j == 0 else "") + ": pool slots in use"
            ax.plot(xs, ys, color=POD_COLORS[j], linestyle=POD_STYLES[j], linewidth=2, label=name)
        if "value" in total:
            xs, ys = with_gaps(*total["value"])
            ax.plot(xs, ys, color=NEUTRAL, linewidth=1.6, label="Postgres: actual connections (gaps = refused)")
        ax.set_ylim(0, 165)
        ax.set_yticks(range(0, 101, 20))
        ax.legend(fontsize=7.5, frameon=False, loc="upper left", ncol=1)
        ax.set_xlabel("min:s since start", color=INK2)
    axes[0].set_ylabel("connections", color=INK2)
    fig.suptitle("Database connections. Pool demand per API Pod (Npgsql slots in use, including attempts Postgres "
                 "refused) vs. the server's actual total (30 s samples; refusals come in bursts between them)", x=0.01, ha="left", fontsize=11,
                 color=INK)
    fig.tight_layout(rect=(0, 0, 1, 0.93))
    save(fig, out / "stress-timeline-db-connections.png")

    # ------------------------------------------------------------- 4. CPU: node vs API
    node_q = 'sum(rate(node_cpu_seconds_total{mode!="idle"}[1m]))'
    api_q = ('sum(rate(container_cpu_usage_seconds_total{namespace="m3a",pod=~"m3a-api-.*",'
             'container="api"}[1m]))')
    fig, axes = plt.subplots(1, 2, figsize=(15, 4.4), facecolor=SURFACE)
    for ax, (q, title, cap, cap_label) in zip(axes, [
            (node_q, "Whole VM: CPU busy (cores)", 4, "4 cores in the VM"),
            (api_q, "All API Pods together: CPU used (cores)", 0.3, "cap: 3 Pods × 100m = 0.3")]):
        style(ax, title)
        step_marks(ax)
        ax.axhline(cap, color=INK, linewidth=1)
        ax.text(5, cap * 1.03, cap_label, fontsize=7.5, color=INK)
        for i, run in enumerate(runs):
            s = series(prom(args.prom, q, run["start"], run["end"]), run["start"])
            if "value" in s:
                xs, ys = s["value"]
                ax.plot(xs, ys, color=RUN_COLORS[i], linewidth=1.8, label=run["label"],
                        marker=RUN_MARKERS[i], markevery=6, markersize=6,
                        markeredgecolor=SURFACE, markeredgewidth=1.5)
        ax.set_ylim(0, cap * 1.15)
        ax.set_xlabel("min:s since start", color=INK2)
    fig.legend(*axes[0].get_legend_handles_labels(), loc="upper center", ncol=len(runs), frameon=False,
               bbox_to_anchor=(0.5, 1.0), fontsize=10)
    fig.tight_layout(rect=(0, 0, 1, 0.92))
    save(fig, out / "stress-timeline-cpu.png")


if __name__ == "__main__":
    main()
