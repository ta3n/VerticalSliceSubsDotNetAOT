#!/usr/bin/env bash
# Load test against the stack started with `podman compose up -d --build`. Runs k6 in a container on the host network.
#   scripts/load-test.sh                       # defaults: http://localhost:8080, 50 VUs, 20 s per scenario
#   VUS=100 DURATION=30s scripts/load-test.sh
#   BASE_URL=http://localhost:9090 scripts/load-test.sh
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
engine=${CONTAINER_ENGINE:-podman}
base=${BASE_URL:-http://localhost:8080}
curl -fsS "$base/health" >/dev/null || { echo "API not reachable at $base - run: podman compose up -d --build" >&2; exit 1; }
mkdir -p "$root/artifacts"
"$engine" run --rm --user 0:0 --network host -v "$root/scripts:/scripts:ro,z" -v "$root/artifacts:/out:z" \
  -e BASE_URL="$base" -e VUS="${VUS:-50}" -e DURATION="${DURATION:-20s}" \
  docker.io/grafana/k6:latest run --summary-export=/out/load-test-summary.json /scripts/load-test.js
