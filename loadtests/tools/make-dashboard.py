#!/usr/bin/env python3
"""Generates kubernetes/monitoring/stress-test-dashboard.yml: the Grafana dashboard used to watch
and capture the stress-test runs ("M3A — Prueba de estrés", uid m3a-stress).

    python3 loadtests/tools/make-dashboard.py && kubectl apply -f kubernetes/monitoring/stress-test-dashboard.yml

The chart's dashboard sidecar loads ConfigMaps labelled grafana_dashboard: "1" from any namespace.
Panel ids are stable (capture-dashboards.py screenshots them one by one with /d-solo).
"""

from __future__ import annotations

import json
from pathlib import Path

DS = {"type": "prometheus", "uid": "prometheus"}
API = 'namespace="m3a",pod=~"m3a-api-.*"'
ROUTES = 'namespace="m3a",http_route!~"/metrics|/health.*"'

# (id, title, unit, [(expr, legend)], description)
PANELS = [
    (1, "API — peticiones/s por ruta (servidor)", "reqps",
     [(f'sum by (http_request_method, http_route) (rate(http_server_request_duration_seconds_count{{{ROUTES}}}[1m]))',
       "{{http_request_method}} {{http_route}}")], ""),
    (2, "API — latencia p95 por ruta (servidor)", "s",
     [(f'histogram_quantile(0.95, sum by (le, http_request_method, http_route) (rate(http_server_request_duration_seconds_bucket{{{ROUTES}}}[1m])))',
       "{{http_request_method}} {{http_route}}")], "No incluye el tiempo en cola antes de Kestrel."),
    (3, "API — respuestas/s por código", "reqps",
     [(f'sum by (http_response_status_code) (rate(http_server_request_duration_seconds_count{{{ROUTES}}}[1m]))',
       "{{http_response_status_code}}")], ""),
    (4, "API — CPU por Pod (límite 0.1 cores)", "none",
     [(f'sum by (pod) (rate(container_cpu_usage_seconds_total{{{API},container="api"}}[1m]))', "{{pod}}"),
      ('max(kube_pod_container_resource_limits{namespace="m3a",container="api",resource="cpu"})', "límite")], "Cores."),
    (5, "API — fracción de periodos CFS con throttling", "percentunit",
     [(f'sum by (pod) (rate(container_cpu_cfs_throttled_periods_total{{{API},container="api"}}[1m])) / '
       f'sum by (pod) (rate(container_cpu_cfs_periods_total{{{API},container="api"}}[1m]))', "{{pod}}")], ""),
    (6, "API — memoria (working set) por Pod", "bytes",
     [(f'sum by (pod) (container_memory_working_set_bytes{{{API},container="api"}})', "{{pod}}")], ""),
    (7, "API — réplicas disponibles", "none",
     [('kube_deployment_status_replicas_available{namespace="m3a",deployment="m3a-api"}', "disponibles"),
      ('kube_deployment_spec_replicas{namespace="m3a",deployment="m3a-api"}', "deseadas")], ""),
    (8, "API — conexiones Kestrel por Pod", "none",
     [('sum by (pod) (kestrel_active_connections{namespace="m3a"})', "{{pod}}")],
     "Con balanceo por conexión (NodePort) muestra si los clientes quedan pegados a un Pod."),
    (9, "Npgsql — conexiones del pool por Pod y estado", "none",
     [('sum by (pod, db_client_connection_state) (db_client_connection_count{namespace="m3a"})',
       "{{pod}} {{db_client_connection_state}}"),
      ('sum by (pod) (db_client_connection_npgsql_pending_requests{namespace="m3a"})', "{{pod}} en espera")], ""),
    (10, "PostgreSQL — CPU (cores)", "none",
     [('sum(rate(container_cpu_usage_seconds_total{namespace="m3a",pod="m3a-db-0",container!=""}[1m]))', "m3a-db-0")], ""),
    (11, "PostgreSQL — seq scans vs index scans por tabla", "ops",
     [('sum by (relname) (rate(pg_stat_user_tables_seq_scan{datname="m3a_db"}[1m]))', "seq {{relname}}"),
      ('sum by (relname) (rate(pg_stat_user_tables_idx_scan{datname="m3a_db"}[1m]))', "idx {{relname}}")],
     "Sin índices de FK, cada búsqueda por EventId es un seq scan de tickets."),
    (12, "PostgreSQL — filas leídas por seq scan /s", "rowsps",
     [('sum by (relname) (rate(pg_stat_user_tables_seq_tup_read{datname="m3a_db"}[1m]))', "{{relname}}")], ""),
    (13, "PostgreSQL — top SQL por tiempo de ejecución (pg_stat_statements)", "percentunit",
     [('topk(8, sum by (queryid) (rate(pg_stat_statements_seconds_total{datname="m3a_db"}[1m])))', "{{queryid}}")],
     "Segundos de ejecución por segundo. El texto de cada queryid está en pg-stat-statements.txt de la corrida."),
    (14, "Traefik — peticiones/s por servicio y código", "reqps",
     [('sum by (service, code) (rate(traefik_service_requests_total{service=~"m3a-.*"}[1m]))', "{{service}} {{code}}")],
     "Vacío en corridas directas (NodePort)."),
    (15, "Traefik — CPU (cores)", "none",
     [('sum by (pod) (rate(container_cpu_usage_seconds_total{namespace="traefik",container!=""}[1m]))', "{{pod}}")], ""),
    (16, "Nodo kind — CPU ocupada (cores de 4)", "none",
     [('sum(rate(node_cpu_seconds_total{mode!="idle"}[1m]))', "nodo")], ""),
]


def panel(pid, title, unit, targets, description):
    i = pid - 1
    return {
        "id": pid, "type": "timeseries", "title": title, "description": description, "datasource": DS,
        "gridPos": {"h": 8, "w": 12, "x": (i % 2) * 12, "y": (i // 2) * 8},
        "fieldConfig": {
            "defaults": {"unit": unit, "custom": {"lineWidth": 2, "fillOpacity": 5, "showPoints": "never"}},
            "overrides": [{"matcher": {"id": "byName", "options": "límite"},
                           "properties": [{"id": "custom.lineStyle", "value": {"fill": "dash", "dash": [8, 6]}},
                                          {"id": "color", "value": {"mode": "fixed", "fixedColor": "red"}}]}],
        },
        "options": {"legend": {"displayMode": "table", "placement": "right", "calcs": ["mean", "max"]},
                    "tooltip": {"mode": "multi", "sort": "desc"}},
        "targets": [{"refId": chr(65 + n), "datasource": DS, "expr": e, "legendFormat": l, "range": True}
                    for n, (e, l) in enumerate(targets)],
    }


dashboard = {
    "uid": "m3a-stress", "title": "M3A — Prueba de estrés", "tags": ["m3a", "stress-test"],
    "timezone": "browser", "schemaVersion": 41, "refresh": "10s", "editable": True,
    "time": {"from": "now-30m", "to": "now"},
    "panels": [panel(*p) for p in PANELS],
}

out = Path(__file__).resolve().parents[2] / "kubernetes/monitoring/stress-test-dashboard.yml"
body = json.dumps(dashboard, ensure_ascii=False, indent=1)
out.write_text(
    "# GENERATED by loadtests/tools/make-dashboard.py — edit that script, not this file.\n"
    "# Grafana dashboard for the stress-test runs, loaded by the chart's dashboard sidecar.\n"
    "apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: m3a-stress-dashboard\n  namespace: monitoring\n"
    "  labels:\n    grafana_dashboard: \"1\"\ndata:\n  m3a-stress.json: |\n"
    + "\n".join("    " + line for line in body.splitlines()) + "\n")
print(f"wrote {out}")
