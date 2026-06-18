# Grafana provisioning

`aethernet-overview.json` is a starter dashboard for the metrics each service emits.

To use it:

1. Add Prometheus as a Grafana data source (scrape `/metrics` from your auth, hub, and file servers).
2. Dashboards → Import → Upload JSON file → select `aethernet-overview.json`.
3. Pick your Prometheus data source when prompted.

Provisioning a managed Grafana? Drop the file into `/etc/grafana/provisioning/dashboards/`
and add a matching `dashboards.yaml` provider.
