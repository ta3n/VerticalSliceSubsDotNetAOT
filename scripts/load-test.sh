#!/usr/bin/env bash
# Load test against the running stack. Runs k6 in a container on the host network. Settings come from scripts/.env
# (local k8s via Kong) or ENV_FILE=scripts/.env.compose (compose); explicit variables win over both.
#   scripts/load-test.sh                       # BASE_URL/VUS/DURATION from scripts/.env
#   VUS=100 DURATION=30s scripts/load-test.sh
#   BASE_URL=http://localhost:9090 scripts/load-test.sh
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
# shellcheck source=env.sh
source "$root/scripts/env.sh"   # scripts/.env (local k8s) unless ENV_FILE says otherwise
engine=${CONTAINER_ENGINE:-podman}
base=${BASE_URL:-http://localhost:8080}
curl -fsS "$base/health" >/dev/null || { echo "API not reachable at $base - start the stack (task up / podman compose up -d --build) or check BASE_URL in ${ENV_FILE:-scripts/.env}" >&2; exit 1; }
mkdir -p "$root/artifacts"
"$engine" run --rm --user "$(container_user)" --network host -v "$root/scripts:/scripts:ro,z" -v "$root/artifacts:/out:z" \
  -e BASE_URL="$base" -e VUS="${VUS:-50}" -e DURATION="${DURATION:-20s}" \
  docker.io/grafana/k6:latest run --summary-export=/out/load-test-summary.json /scripts/load-test.js
