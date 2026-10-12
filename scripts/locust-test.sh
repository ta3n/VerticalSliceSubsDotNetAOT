#!/usr/bin/env bash
# Locust load test against the running stack (podman compose up -d --build). Runs Locust headless in a container
# on the host network and writes an HTML report + CSVs to artifacts/locust/.
#   scripts/locust-test.sh                                   # 200 users, 60 s
#   USERS=1000 SPAWN_RATE=200 RUN_TIME=2m scripts/locust-test.sh
#   SHAPE=step STEP_USERS=250 STEP_SECONDS=20 MAX_USERS=4000 scripts/locust-test.sh   # step-up ladder
#   WEB=1 scripts/locust-test.sh                             # Locust web UI on http://localhost:8089
#   PROCESSES=8 ...                                          # fork N worker processes (one Locust process saturates one core)
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
engine=${CONTAINER_ENGINE:-podman}; base=${BASE_URL:-http://localhost:8080}
curl -fsS "$base/health" >/dev/null || { echo "API not reachable at $base - run: podman compose up -d --build" >&2; exit 1; }
mkdir -p "$root/artifacts/locust"
args=(-f /mnt/locust/locustfile.py --host "$base" --processes "${PROCESSES:-1}")
if [[ -n "${WEB:-}" ]]; then
  echo "Web UI: http://localhost:8089"
else
  args+=(--headless -u "${USERS:-200}" -r "${SPAWN_RATE:-100}" --csv /out/locust --html /out/report.html --only-summary)
  [[ -z "${SHAPE:-}" ]] && args+=(-t "${RUN_TIME:-60s}")
fi
"$engine" run --rm --user 0:0 --network host --ulimit nofile="$(ulimit -Hn):$(ulimit -Hn)" \
  -v "$root/scripts/locust:/mnt/locust:ro,z" -v "$root/artifacts/locust:/out:z" \
  -e SHAPE="${SHAPE:-}" -e STEP_USERS="${STEP_USERS:-250}" -e STEP_SECONDS="${STEP_SECONDS:-20}" -e MAX_USERS="${MAX_USERS:-4000}" \
  docker.io/locustio/locust:latest "${args[@]}"
echo "report: artifacts/locust/report.html  csv: artifacts/locust/locust_stats.csv"
