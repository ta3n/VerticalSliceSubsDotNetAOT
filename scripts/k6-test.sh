#!/usr/bin/env bash
# Runs one k6 scenario (scripts/<name>-test.js) against the running stack, in a container on the host network.
#   scripts/k6-test.sh smoke                      # every endpoint + error contract, a few iterations
#   scripts/k6-test.sh load                       # per-endpoint load (same as scripts/load-test.sh)
#   scripts/k6-test.sh stress                     # one constant-VU step (the ladder is scripts/stress-test.sh)
#   SPIKE_VUS=800 scripts/k6-test.sh spike        # steady users + two bursts
#   SOAK_DURATION=1h scripts/k6-test.sh soak      # constant rate for a long time
#   scripts/k6-test.sh rampup                     # VUs grow 50 -> 2000, hold, leave: where does latency degrade?
#   scripts/k6-test.sh breakpoint                 # ramp the rate until it breaks (exit 99 = breaking point found)
#   scripts/k6-test.sh contention                 # races on the same row: exactly one winner
#   scripts/k6-test.sh pagination                 # keyset paging: no duplicates, stable order, flat latency
#   scripts/k6-test.sh saga                       # renewal success, decline, timeout + cancel/refund
#   S2S=true scripts/k6-test.sh endpoints         # every route in parallel flows (S2S=true adds Billing/Payments routes)
#   BIGDATA_PROFILE=small scripts/k6-test.sh bigdata   # big dataset in phases (seed, renew, cancel, read); small|medium|large
#   KEYCLOAK_URL=http://localhost:8081 KEYCLOAK_CLIENT_SECRET=... scripts/k6-test.sh smoke   # with a bearer token (loadtest client)
# Settings: scripts/.env (local k8s), ENV_FILE=scripts/.env.compose or ENV_FILE=scripts/.env.aspire; explicit variables
# win. Each script documents its own knobs in its header. Extra arguments go to `k6 run`.
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
name=${1:-}; [[ $# -gt 0 ]] && shift
script="$root/scripts/$name-test.js"
if [[ -z $name || ! -f $script ]]; then
  echo "usage: scripts/k6-test.sh <$(cd "$root/scripts" && ls ./*-test.js | sed 's#^\./##; s#-test\.js$##' | paste -sd'|')> [k6 run args]" >&2
  exit 2
fi
# shellcheck source=env.sh
source "$root/scripts/env.sh"
engine=${CONTAINER_ENGINE:-podman}
base=${BASE_URL:-http://localhost:8080}
curl -fsS "$base/health" >/dev/null || { echo "API not reachable at $base - start the stack or check BASE_URL in ${ENV_FILE:-scripts/.env}" >&2; exit 1; }
mkdir -p "$root/artifacts/k6"

# Forward BASE_URL plus every knob the scenarios read (only the ones that are set).
knobs=(-e "BASE_URL=$base")
for var in VUS DURATION HTTP_TIMEOUT ITERATIONS RENEWAL_TIMEOUT_S BILLING_URL PAYMENTS_URL REPORTING_URL S2S KEYCLOAK_URL KEYCLOAK_REALM KEYCLOAK_CLIENT_ID KEYCLOAK_CLIENT_SECRET $(compgen -v | grep -E '^(SMOKE|SPIKE|SOAK|RAMPUP|BP|CONTENTION|PAGINATION|ENDPOINTS|BIGDATA)_' || true); do
  [[ -n ${!var+x} ]] && knobs+=(-e "$var=${!var}")
done
mapfile -t rw_env < <(k6_prometheus_args)
mapfile -t rw_out < <(k6_output_args "$name")

"$engine" run --rm --user "$(container_user)" --network host --ulimit nofile="$(ulimit -Hn):$(ulimit -Hn)" \
  -v "$root/scripts:/scripts:ro,z" -v "$root/artifacts/k6:/out:z" "${knobs[@]}" "${rw_env[@]}" \
  docker.io/grafana/k6:latest run "${rw_out[@]}" --summary-export="/out/$name-summary.json" "$@" "/scripts/$name-test.js"
