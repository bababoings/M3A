#!/usr/bin/env python3
"""Comparison charts of constant-load runs against the baseline, for the report.

    .venv-loadtests/bin/python loadtests/tools/compare-runs.py

Reads the per-step row and the journeys line of each run's summary.md (summarize.py) and writes
docs/stress-test/img/comparativa-throughput.png, comparativa-latencia.png and comparativa-rampas.png
(req/s OK per step of the stepped ramps).
"""

from __future__ import annotations

import re
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
RESULTS = ROOT / "loadtests/results"
OUT = ROOT / "docs/stress-test/img"
BASELINE = "02b-base-sin-indices-spawn1"
# (run, label, group) — group picks the color: base, bd, directa, traefik
RUNS = [
    (BASELINE, "Línea base\n(sin índices)", "base"),
    ("04a-indices", "4a índices", "bd"),
    ("04b-filtro-directa", "4b filtro\ndirecta", "directa"),
    ("04b-filtro-traefik", "4b filtro\nTraefik", "traefik"),
    *[(f"05-{n}rep-{p}", f"{n} rép.\n{'directa' if p == 'directa' else 'Traefik'}", p)
      for n in (1, 2, 3, 4) for p in ("directa", "traefik")],
]
COLORS = {"base": "#8a8a8a", "bd": "#c98b2e", "directa": "#2f6db5", "traefik": "#2e9a6b"}


def load(run: str) -> dict[str, float]:
    text = (RESULTS / run / "summary.md").read_text()
    row = next(line for line in text.splitlines() if re.match(r"^\| \d+ \|", line))
    cells = [c.strip().replace(",", "").rstrip("%") for c in row.strip("|").split("|")]
    journeys = re.search(r"Journeys completados:\*\* (\d+)", text)
    return {"ok_rps": float(cells[2]), "fail": float(cells[3]), "p50": float(cells[4]),
            "p95": float(cells[5]), "p99": float(cells[6]),
            "journeys": float(journeys.group(1)) if journeys else 0.0}


def steps(run: str) -> list[tuple[int, float, float]]:
    """(users, ok req/s, p95 ms) for every row of a run's per-step table."""
    out = []
    for line in (RESULTS / run / "summary.md").read_text().splitlines():
        if re.match(r"^\| \d+ \|", line):
            c = [x.strip().replace(",", "") for x in line.strip("|").split("|")]
            out.append((int(c[0]), float(c[2]), float(c[5])))
    return out


# (label, runs concatenated in order, color, line style)
RAMPS = [
    ("1 réplica, sin optimizar (fase 1)", ["01-rampa-sin-indices"], COLORS["base"], "-"),
    ("4 rép. directa, pool por defecto (fase 7)", ["08-rampa-4rep-directa"], COLORS["directa"], ":"),
    ("4 rép. Traefik, pool por defecto (fase 7)", ["08-rampa-4rep-traefik"], COLORS["traefik"], ":"),
    ("4 rép. directa, pool 20 (fases 8 y 8b)", ["09-rampa-4rep-pool20-directa", "10-rampa-alta-4rep-directa"], COLORS["directa"], "-"),
    ("4 rép. Traefik, pool 20 (fases 8 y 8b)", ["09-rampa-4rep-pool20-traefik", "10-rampa-alta-4rep-traefik"], COLORS["traefik"], "-"),
]


def ramps_chart() -> None:
    fig, (a, b) = plt.subplots(1, 2, figsize=(14, 5.2), constrained_layout=True)
    for label, runs, color, ls in RAMPS:
        if not all((RESULTS / r / "summary.md").exists() for r in runs):
            continue
        pts = [p for r in runs for p in steps(r)]
        a.plot([u for u, _, _ in pts], [ok for _, ok, _ in pts], ls, color=color, marker="o", ms=4, label=label)
        b.plot([u for u, _, _ in pts], [p95 for *_, p95 in pts], ls, color=color, marker="o", ms=4, label=label)
    a.set_title("Rampas: req/s OK por escalón")
    a.set_xlabel("usuarios concurrentes")
    a.set_ylabel("req/s OK")
    b.set_title("Rampas: latencia p95 por escalón (escala log)")
    b.set_xlabel("usuarios concurrentes")
    b.set_ylabel("ms")
    b.set_yscale("log")
    for ax in (a, b):
        ax.grid(alpha=0.3)
        ax.spines[["top", "right"]].set_visible(False)
    a.legend(fontsize=8)
    fig.savefig(OUT / "comparativa-rampas.png", dpi=130)


def bars(ax, labels, values, colors, fmt, log=False):
    b = ax.bar(range(len(values)), values, color=colors)
    ax.set_xticks(range(len(values)), labels, fontsize=8)
    if log:
        ax.set_yscale("log")
    for rect, v in zip(b, values):
        ax.annotate(fmt(v), (rect.get_x() + rect.get_width() / 2, rect.get_height()),
                    ha="center", va="bottom", fontsize=8, xytext=(0, 2), textcoords="offset points")
    ax.spines[["top", "right"]].set_visible(False)
    ax.grid(axis="y", alpha=0.3)


def main() -> None:
    runs = [(r, label, g, load(r)) for r, label, g in RUNS if (RESULTS / r / "summary.md").exists()]
    labels = [label for _, label, _, _ in runs]
    colors = [COLORS[g] for _, _, g, _ in runs]
    base = next(m for r, _, _, m in runs if r == BASELINE)

    fig, (a, b) = plt.subplots(2, 1, figsize=(13, 8.5), constrained_layout=True)
    bars(a, labels, [m["ok_rps"] for *_, m in runs], colors,
         lambda v: f"{v:.0f}\n({v / base['ok_rps']:.0f}×)" if v >= 10 else f"{v:.1f}")
    a.set_title("Peticiones exitosas por segundo — 16 usuarios, 5 min, API con 100m de CPU por Pod")
    a.set_ylabel("req/s OK")
    a.set_ylim(0, max(m["ok_rps"] for *_, m in runs) * 1.18)
    bars(b, labels, [m["journeys"] for *_, m in runs], colors, lambda v: f"{v:,.0f}")
    b.set_title("Journeys completados en 5 min")
    b.set_ylim(0, max(m["journeys"] for *_, m in runs) * 1.12)
    fig.savefig(OUT / "comparativa-throughput.png", dpi=130)

    fig, ax = plt.subplots(figsize=(13, 5), constrained_layout=True)
    bars(ax, labels, [m["p95"] for *_, m in runs], colors, lambda v: f"{v:,.0f}", log=True)
    ax.set_title("Latencia p95 (ms, escala log) — la línea discontinua es el periodo CFS de 100 ms")
    ax.axhline(100, color="red", ls="--", lw=1)
    ax.set_ylabel("ms")
    fig.savefig(OUT / "comparativa-latencia.png", dpi=130)
    ramps_chart()
    for r, _, _, m in runs:
        print(f"{r:32s} {m['ok_rps']:7.1f} req/s OK  {m['fail']:5.1f}% fallos  p95 {m['p95']:7.0f}  journeys {m['journeys']:.0f}")


if __name__ == "__main__":
    main()
