#!/usr/bin/env bash
# Step-up stress ladder: one k6 run per VU level against the running stack, summary table at the end.
# Settings: scripts/.env (local k8s) or ENV_FILE=scripts/.env.compose; explicit variables win.
#   LEVELS="100 500 1000 2000 4000 8000" DURATION=20s scripts/stress-test.sh
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
# shellcheck source=env.sh
source "$root/scripts/env.sh"   # scripts/.env (local k8s) unless ENV_FILE says otherwise
engine=${CONTAINER_ENGINE:-podman}; base=${BASE_URL:-http://localhost:8080}
curl -fsS "$base/health" >/dev/null || { echo "API not reachable at $base - start the stack (task up / podman compose up -d --build) or check BASE_URL in ${ENV_FILE:-scripts/.env}" >&2; exit 1; }
mkdir -p "$root/artifacts/stress"
levels=${LEVELS:-100 250 500 1000 2000 4000 8000}
for v in $levels; do
  "$engine" run --rm --user "$(container_user)" --network host --ulimit nofile=$(ulimit -Hn):$(ulimit -Hn) \
    -v "$root/scripts:/scripts:ro,z" -v "$root/artifacts/stress:/out:z" \
    -e BASE_URL="$base" -e VUS="$v" -e DURATION="${DURATION:-20s}" \
    docker.io/grafana/k6:latest run --quiet --summary-export="/out/vus-$v.json" /scripts/stress-test.js >"$root/artifacts/stress/k6-$v.log" 2>&1 || true
  sleep 3
done
# Only this run's levels: artifacts/stress also keeps results of earlier runs (other levels, other targets).
python3 - "$root/artifacts/stress" $levels <<'PY'
import json,os,re,sys
rows=[p for p in (f"{sys.argv[1]}/vus-{v}.json" for v in sorted(set(sys.argv[2:]),key=int)) if os.path.exists(p)]
print(f"{'VUs':>6} {'req/s':>8} {'avg ms':>8} {'p95 ms':>8} {'p99 ms':>8} {'max ms':>8} {'fail %':>7}")
for p in rows:
    m=json.load(open(p))['metrics']; d=m['http_req_duration']
    print(f"{re.findall(r'\d+',p)[-1]:>6} {m['http_reqs']['rate']:8.0f} {d['avg']:8.1f} {d['p(95)']:8.1f} {d['p(99)']:8.1f} {d['max']:8.0f} {m['http_req_failed']['value']*100:7.2f}")
PY
