#!/usr/bin/env python3
"""Side-by-side comparison chart of stress runs, from each run's step-summary.csv.

    python3 loadtests/tools/compare-runs.py <out.png> <label>=<run-dir> [<label>=<run-dir> ...]

Three small multiples sharing the user-count axis: successful req/s, failure %, p95 latency (log).
One measure per panel (no dual axes); each panel direct-labels the values where the lines separate,
and every line has a distinct marker plus a legend entry, so identity never depends on colour alone.
"""

from __future__ import annotations

import csv
import sys
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

# Categorical slots 1-3 of the reference palette, validated with the dataviz skill's
# validate_palette.js (light surface #fcfcfb): all checks pass; aqua is under 3:1 contrast,
# so every line is direct-labelled and the report carries the full table.
COLORS = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100"]
MARKERS = ["o", "s", "D", "^"]
SURFACE, INK, INK2, GRID = "#fcfcfb", "#0b0b0b", "#52514e", "#e4e3df"

# (column, title, log scale, value format, where to direct-label: "max", "end" or a step index).
# Labels go where the lines separate, not where they converge.
PANELS = [
    ("ok_req_per_s", "Successful requests / s (peak labelled)", False, "{:.0f}", "max"),
    ("fail_pct", "Failed requests (%)", False, "{:.1f}%", "end"),
    ("p95_ms", "p95 response time (ms, log scale; 100 users labelled)", True, "{:,.0f} ms", 3),
]


def load(run_dir: Path) -> list[dict]:
    with open(run_dir / "step-summary.csv") as f:
        return [{k: float(v) if k != "start_utc" else v for k, v in r.items()} for r in csv.DictReader(f)]


def main() -> None:
    out = Path(sys.argv[1])
    runs = [(arg.split("=", 1)[0], load(Path(arg.split("=", 1)[1]))) for arg in sys.argv[2:]]
    users = [int(r["users"]) for r in runs[0][1]]
    x = list(range(len(users)))  # even spacing: steps are categorical (10, 25, 50, 100 ...)

    plt.rcParams.update({"font.family": "sans-serif", "font.size": 10, "text.color": INK,
                         "axes.labelcolor": INK2, "xtick.color": INK2, "ytick.color": INK2})
    fig, axes = plt.subplots(1, 3, figsize=(15, 4.8), facecolor=SURFACE)
    for ax, (key, title, log, fmt, at) in zip(axes, PANELS):
        ax.set_facecolor(SURFACE)
        for i, (label, rows) in enumerate(runs):
            ys = [r[key] for r in rows]
            ax.plot(x, ys, color=COLORS[i], linewidth=2, solid_capstyle="round", solid_joinstyle="round",
                    marker=MARKERS[i], markersize=7, markeredgecolor=SURFACE, markeredgewidth=2, zorder=3)
            j = ys.index(max(ys)) if at == "max" else (len(ys) - 1 if at == "end" else at)
            # Text in ink, never the series colour; a surface-coloured box keeps it legible over lines.
            ax.annotate(fmt.format(ys[j]), xy=(x[j], ys[j]), xytext=(9, 0), textcoords="offset points",
                        fontsize=9, color=INK, va="center", zorder=4,
                        bbox=dict(boxstyle="round,pad=0.15", facecolor=SURFACE, edgecolor="none"))
        if log:
            ax.set_yscale("log")
        ax.set_title(title, loc="left", fontsize=10.5, color=INK, pad=10)
        ax.set_xticks(x, [str(u) for u in users])
        ax.set_xlabel("concurrent users (90 s per step)")
        ax.grid(axis="y", color=GRID, linewidth=1)
        ax.set_axisbelow(True)
        for side in ("top", "right"):
            ax.spines[side].set_visible(False)
        for side in ("left", "bottom"):
            ax.spines[side].set_color(GRID)
        ax.tick_params(length=0)
        ax.set_xlim(-0.4, x[-1] + 0.9)

    handles = [plt.Line2D([], [], color=COLORS[i], marker=MARKERS[i], linewidth=2, markersize=7,
                          markeredgecolor=SURFACE, markeredgewidth=2) for i in range(len(runs))]
    fig.legend(handles, [label for label, _ in runs], loc="upper center", ncol=len(runs), frameon=False,
               bbox_to_anchor=(0.5, 1.0), fontsize=10)
    fig.tight_layout(rect=(0, 0, 1, 0.92))
    out.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(out, dpi=130, facecolor=SURFACE)
    print(out)


if __name__ == "__main__":
    main()
