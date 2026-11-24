#!/usr/bin/env python3
"""Screenshots of Grafana and Prometheus over one run's exact time window, for the report.

    .venv-loadtests/bin/python loadtests/tools/capture-dashboards.py loadtests/results/<run>

Reads <run>/start_epoch and <run>/end_epoch and writes <run>/capturas/:
  grafana-m3a-stress.png            the whole "M3A — Prueba de estrés" dashboard (make-dashboard.py)
  grafana-panel-NN-<slug>.png       each of its panels, cropped from that same page
  grafana-k8s-workload.png          bundled "Kubernetes / Compute Resources / Workload", m3a-api
  prometheus-<slug>.png             Prometheus' own graph UI for a few key expressions

Grafana's admin password is read from the monitoring-grafana Secret at run time (never stored).
Needs playwright (`pip install playwright && python -m playwright install chromium`).
"""

from __future__ import annotations

import argparse
import base64
import re
import subprocess
import urllib.parse
from datetime import datetime, timezone
from pathlib import Path

from playwright.sync_api import sync_playwright

GRAFANA = "http://127.0.0.1:3000"
PROM = "http://127.0.0.1:9090"
WORKLOAD_UID = "a164a7f0339f99e89cea5cb47e9be617"  # Kubernetes / Compute Resources / Workload
PANELS = {
    1: "rps-por-ruta", 2: "p95-por-ruta", 3: "respuestas-por-codigo", 4: "api-cpu", 5: "api-throttling",
    6: "api-memoria", 7: "replicas", 8: "kestrel-conexiones", 9: "npgsql-pool", 10: "db-cpu",
    11: "seq-vs-idx-scans", 12: "seq-tup-read", 13: "top-sql", 14: "traefik-rps", 15: "traefik-cpu",
    16: "nodo-cpu",
}
API = 'namespace="m3a",pod=~"m3a-api-.*"'
PROM_GRAPHS = {
    "api-cpu": f'sum by (pod) (rate(container_cpu_usage_seconds_total{{{API},container="api"}}[1m]))',
    "api-rps": 'sum(rate(http_server_request_duration_seconds_count{namespace="m3a",http_route!~"/metrics|/health.*"}[1m]))',
    "api-p95": 'histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket{namespace="m3a",http_route!~"/metrics|/health.*"}[1m])))',
    "tickets-seq-scan": 'rate(pg_stat_user_tables_seq_tup_read{datname="m3a_db",relname="tickets"}[1m])',
}


def grafana_password() -> str:
    raw = subprocess.check_output(["kubectl", "-n", "monitoring", "get", "secret", "monitoring-grafana",
                                   "-o", "jsonpath={.data.admin-password}"])
    return base64.b64decode(raw).decode()


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("run_dir", type=Path)
    ap.add_argument("--pad", type=int, default=30, help="seconds added before and after the run")
    args = ap.parse_args()
    run = args.run_dir
    start = int((run / "start_epoch").read_text()) - args.pad
    end = int((run / "end_epoch").read_text()) + args.pad
    out = run / "capturas"
    out.mkdir(exist_ok=True)
    rng = f"from={start * 1000}&to={end * 1000}"

    with sync_playwright() as p:
        browser = p.chromium.launch()
        ctx = browser.new_context(device_scale_factor=1.5, color_scheme="light")
        page = ctx.new_page()
        # Grafana redirects to its login form instead of answering 401, so log in once; the
        # session cookie covers every dashboard below.
        page.goto(f"{GRAFANA}/login", wait_until="load", timeout=60_000)
        page.fill('input[name="user"]', "admin")
        page.fill('input[name="password"]', grafana_password())
        page.click('button[type="submit"]')
        page.wait_for_url(lambda url: "/login" not in url, timeout=60_000)

        def shot(url: str, path: Path, width: int, height: int, settle_ms: int = 2500) -> None:
            page.set_viewport_size({"width": width, "height": height})
            # Not "networkidle": Grafana keeps a live connection open, so the network never idles.
            page.goto(url, wait_until="load", timeout=60_000)
            page.wait_for_timeout(settle_ms)
            try:  # Grafana shows a spinner per panel while its queries run
                page.wait_for_selector('[aria-label="Panel loading bar"]', state="detached", timeout=20_000)
            except Exception:  # noqa: BLE001 — not every page has panels
                pass
            page.screenshot(path=str(path), full_page=True)
            print(f"  {path.name}")

        print(f"capturing {run.name} ({datetime.fromtimestamp(start, timezone.utc):%H:%M:%S}–"
              f"{datetime.fromtimestamp(end, timezone.utc):%H:%M:%S} UTC)")
        # One load of the dashboard, then each panel cropped from the page: loading every panel with
        # /d-solo re-runs the whole dashboard each time and can overload Grafana on the kind node.
        shot(f"{GRAFANA}/d/m3a-stress?orgId=1&{rng}&kiosk&theme=light", out / "grafana-m3a-stress.png",
             1800, 2750, 20000)
        for pid, slug in PANELS.items():
            panel = page.locator(f'[data-viz-panel-key="panel-{pid}"]')
            if panel.count():
                panel.first.screenshot(path=str(out / f"grafana-panel-{pid:02d}-{slug}.png"))
                print(f"  grafana-panel-{pid:02d}-{slug}.png")
            else:
                print(f"  panel {pid} not found on the page")
        page.wait_for_timeout(5000)  # give Grafana a breather before the next dashboard
        shot(f"{GRAFANA}/d/{WORKLOAD_UID}?orgId=1&{rng}&kiosk&theme=light&var-datasource=prometheus"
             f"&var-namespace=m3a&var-type=deployment&var-workload=m3a-api",
             out / "grafana-k8s-workload.png", 1800, 1600, 20000)

        span = end - start
        end_input = datetime.fromtimestamp(end, timezone.utc).strftime("%Y-%m-%d %H:%M:%S")
        for slug, expr in PROM_GRAPHS.items():
            qs = urllib.parse.urlencode({"g0.expr": expr, "g0.tab": "graph", "g0.show_tree": "0",
                                         "g0.range_input": f"{span}s", "g0.end_input": end_input,
                                         "g0.moment_input": end_input})
            shot(f"{PROM}/query?{qs}", out / f"prometheus-{re.sub('[^a-z0-9-]', '', slug)}.png", 1500, 900, 4000)
        browser.close()


if __name__ == "__main__":
    main()
