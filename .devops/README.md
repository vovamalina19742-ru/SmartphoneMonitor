Local monitoring stack (Prometheus + Grafana)

Quick start:

1. Ensure Docker Desktop is running and `host.docker.internal` is resolvable (Windows/macOS).
2. From this project's `.devops` folder run:

```bash
docker-compose up -d
```

3. Open Prometheus at http://localhost:9090 and Grafana at http://localhost:3000 (login admin/admin).
4. Add Prometheus as a data source in Grafana (URL: http://prometheus:9090 or http://host.docker.internal:9090 if adding externally).
5. Import the provided dashboard: in Grafana go to Create -> Import and upload `grafana_dashboard.json` from this folder.
6. The dashboard includes panels for `promo_matches_total`, `promo_matches_brand_*`, `hotdeals_current`, and average `analysis_duration_seconds`.

Notes:
- The application exposes metrics at `http://localhost:9184/metrics` (default). Prometheus scrapes using `host.docker.internal` inside Docker.
- If `host.docker.internal` is not available on your platform, adjust `prometheus.yml` to point to the host IP.
