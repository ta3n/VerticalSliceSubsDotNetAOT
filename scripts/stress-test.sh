#!/usr/bin/env bash
# Step-up stress ladder: one k6 run per VU level against the running stack, summary table at the end.
#   LEVELS="100 500 1000 2000 4000 8000" DURATION=20s scripts/stress-test.sh
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
engine=${CONTAINER_ENGINE:-podman}; base=${BASE_URL:-http://localhost:8080}
curl -fsS "$base/health" >/dev/null || { echo "API not reachable at $base" >&2; exit 1; }
mkdir -p "$root/artifacts/stress"
for v in ${LEVELS:-100 250 500 1000 2000 4000 8000}; do
  "$engine" run --rm --user 0:0 --network host --ulimit nofile=$(ulimit -Hn):$(ulimit -Hn) \
    -v "$root/scripts:/scripts:ro,z" -v "$root/artifacts/stress:/out:z" \
    -e BASE_URL="$base" -e VUS="$v" -e DURATION="${DURATION:-20s}" \
    docker.io/grafana/k6:latest run --quiet --summary-export="/out/vus-$v.json" /scripts/stress-test.js >"$root/artifacts/stress/k6-$v.log" 2>&1 || true
  sleep 3
done
python3 - "$root/artifacts/stress" <<'PY'
import json,glob,re,sys
rows=sorted(glob.glob(sys.argv[1]+'/vus-*.json'),key=lambda p:int(re.findall(r'\d+',p)[-1]))
print(f"{'VUs':>6} {'req/s':>8} {'avg ms':>8} {'p95 ms':>8} {'p99 ms':>8} {'max ms':>8} {'fail %':>7}")
for p in rows:
    m=json.load(open(p))['metrics']; d=m['http_req_duration']
    print(f"{re.findall(r'\d+',p)[-1]:>6} {m['http_reqs']['rate']:8.0f} {d['avg']:8.1f} {d['p(95)']:8.1f} {d['p(99)']:8.1f} {d['max']:8.0f} {m['http_req_failed']['value']*100:7.2f}")
PY
