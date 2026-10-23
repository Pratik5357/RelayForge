# Config baked in rather than bind-mounted, so the stack runs from any folder Docker can build
# from (macOS can deny Docker Desktop bind mounts from ~/Desktop or ~/Documents).
FROM prom/prometheus:v3.1.0
COPY prometheus.yml /etc/prometheus/prometheus.yml
