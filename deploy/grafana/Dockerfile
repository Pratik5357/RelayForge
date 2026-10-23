# Provisioning baked in rather than bind-mounted (see deploy/prometheus/Dockerfile).
FROM grafana/grafana:11.4.0
COPY provisioning /etc/grafana/provisioning
COPY dashboards /var/lib/grafana/dashboards
