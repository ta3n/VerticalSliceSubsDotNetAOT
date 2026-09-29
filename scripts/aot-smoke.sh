#!/usr/bin/env bash
# Native AOT smoke test: publish API + migrator natively, start real Postgres/Redis, migrate with the NATIVE
# migrator, run the NATIVE API and exercise the critical flows over HTTP (including OpenAPI/Scalar and OTLP export).
# Exits non-zero on the first failure.
#
#   scripts/aot-smoke.sh                  # publish + run
#   SKIP_PUBLISH=1 scripts/aot-smoke.sh   # reuse artifacts/aot
#   CPP_LINKER=gcc scripts/aot-smoke.sh   # default: clang if installed, else gcc
set -euo pipefail

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
out="$root/artifacts/aot"
port=${PORT:-5480}
rid=${RID:-linux-x64}
linker=${CPP_LINKER:-$(command -v clang >/dev/null && echo clang || echo gcc)}
pg_name="subs-smoke-pg-$$"; redis_name="subs-smoke-redis-$$"
api_pid=""; otlp_pid=""

cleanup() {
  [[ -n "$api_pid" ]] && kill "$api_pid" 2>/dev/null || true
  [[ -n "$otlp_pid" ]] && kill "$otlp_pid" 2>/dev/null || true
  docker rm -fv "$pg_name" "$redis_name" >/dev/null 2>&1 || true
}
trap cleanup EXIT

fail() { echo "FAIL: $*" >&2; [[ -f "$out/api.log" ]] && tail -5 "$out/api.log" | cut -c1-300 >&2; exit 1; }
check() { # check <description> <expected> <actual>
  [[ "$2" == "$3" ]] && echo "ok   $1" || fail "$1 (expected '$2', got '$3')"
}
http() { curl -s -o "$out/body.json" -w '%{http_code}' "$@"; }
json() { python3 -c "import json,sys; d=json.load(open('$out/body.json')); print(eval(sys.argv[1]))" "$1"; }

mkdir -p "$out"
if [[ -z "${SKIP_PUBLISH:-}" ]]; then
  echo "== publishing (linker: $linker)"
  for p in SubscriptionManagement SubscriptionManagement.Migrator; do
    log="$out/publish-$p.log"
    dotnet publish "$root/src/$p" -c Release -r "$rid" -p:CppCompilerAndLinker="$linker" -o "$out/${p##*.}" >"$log" 2>&1 \
      || { tail -20 "$log" >&2; fail "publish of $p failed"; }
    if grep -E "warning|error" "$log" >&2; then fail "publish of $p produced warnings/errors"; fi
  done
fi
api_bin="$out/SubscriptionManagement/SubscriptionManagement"
mig_bin="$out/Migrator/SubscriptionManagement.Migrator"
file "$api_bin" | grep -q ELF || fail "API is not a native executable"

echo "== starting Postgres + Redis"
docker run -d --name "$pg_name" -e POSTGRES_PASSWORD=smoke -e POSTGRES_DB=subs -p 127.0.0.1::5432 postgres:17 >/dev/null
docker run -d --name "$redis_name" -p 127.0.0.1::6379 redis:7-alpine >/dev/null
pg_port=$(docker port "$pg_name" 5432/tcp | head -1 | sed 's/.*://')
redis_port=$(docker port "$redis_name" 6379/tcp | head -1 | sed 's/.*://')
for _ in $(seq 1 60); do docker exec "$pg_name" pg_isready -U postgres -d subs >/dev/null 2>&1 && break; sleep 1; done
sleep 2
pg_cs="Host=127.0.0.1;Port=$pg_port;Database=subs;Username=postgres;Password=smoke"

echo "== native migrator"
"$mig_bin" "$pg_cs" || fail "native migrator failed"
"$mig_bin" "$pg_cs" | grep -q "0 applied" || fail "second migrator run should be a no-op"

echo "== fake OTLP/HTTP receiver"
otlp_port=$((port + 1)); : >"$out/otlp.log"
python3 - "$otlp_port" "$out/otlp.log" <<'PY' &
import sys, http.server
port, log = int(sys.argv[1]), sys.argv[2]
class H(http.server.BaseHTTPRequestHandler):
    def do_POST(self):
        n = int(self.headers.get("content-length", 0)); self.rfile.read(n)
        open(log, "a").write(self.path + "\n")
        self.send_response(200); self.send_header("content-type", "application/x-protobuf"); self.send_header("content-length", "0"); self.end_headers()
    def log_message(self, *a): pass
http.server.HTTPServer(("127.0.0.1", port), H).serve_forever()
PY
otlp_pid=$!

echo "== native API"
# Run from the binary's directory, as the container does, so appsettings.json is found.
ConnectionStrings__Postgres="$pg_cs" ConnectionStrings__Redis="127.0.0.1:$redis_port" ASPNETCORE_URLS="http://127.0.0.1:$port" \
  OpenApi__Enabled=true \
  OTEL_EXPORTER_OTLP_ENDPOINT="http://127.0.0.1:$otlp_port" OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf \
  OTEL_BSP_SCHEDULE_DELAY=500 OTEL_METRIC_EXPORT_INTERVAL=1000 \
  bash -c 'cd "$(dirname "$0")" && exec "$0"' "$api_bin" >"$out/api.log" 2>&1 &
api_pid=$!
for _ in $(seq 1 100); do [[ $(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$port/health/live" || true) == 200 ]] && break; sleep 0.1; done
base="http://127.0.0.1:$port"

check "liveness"  200 "$(http "$base/health/live")"
check "readiness" 200 "$(http "$base/health/ready")"
check "health (db+cache)" 200 "$(http "$base/health")"

check "create subscription" 201 "$(http -XPOST -H 'content-type: application/json' -d '{"serviceName":"Netflix","price":99000,"startDate":"2026-09-05","trialEndsAt":"2026-09-12"}' "$base/subscriptions")"
id=$(json "d['id']")
check "  status is Active(1)" 1 "$(json "d['status']")"
check "get subscription (cached)" 200 "$(http "$base/subscriptions/$id")"
check "pause"  200 "$(http -XPOST "$base/subscriptions/$id/pause")"
check "  status Paused(3)" 3 "$(json "d['status']")"
check "pause again -> 409" 409 "$(http -XPOST "$base/subscriptions/$id/pause")"
check "  problem type" "https://api.demo/errors/invalid-subscription-transition" "$(json "d['type']")"
check "resume" 200 "$(http -XPOST "$base/subscriptions/$id/resume")"
check "renew + invoice" 200 "$(http -XPOST "$base/subscriptions/$id/renew")"
inv=$(json "d['invoice']['id']")
check "  invoice OPEN" OPEN "$(json "d['invoice']['statusLabel']")"
check "list invoices" 200 "$(http "$base/subscriptions/$id/invoices")"
check "pay invoice" 200 "$(http -XPOST "$base/invoices/$inv/pay")"
check "pay again -> 409" 409 "$(http -XPOST "$base/invoices/$inv/pay")"
check "search by status" 200 "$(http -XPOST -H 'content-type: application/json' -d '{"status":"ACTIVE","limit":5}' "$base/subscriptions/search")"
check "search bad cursor -> 400" 400 "$(http -XPOST -H 'content-type: application/json' -d '{"cursor":"!!"}' "$base/subscriptions/search")"
check "validation -> 400" 400 "$(http -XPOST -H 'content-type: application/json' -d '{"serviceName":"","price":0,"startDate":"2026-09-05"}' "$base/subscriptions")"
check "  errors.serviceName present" True "$(json "'serviceName' in d['errors']")"
check "malformed json -> 400" 400 "$(http -XPOST -H 'content-type: application/json' -d '{oops' "$base/subscriptions")"
check "cancel" 204 "$(http -XDELETE "$base/subscriptions/$id")"
check "cancel again -> 400" 400 "$(http -XDELETE "$base/subscriptions/$id")"
check "  legacy error member" "Subscription already canceled." "$(json "d['error']")"
check "unknown subscription -> 404" 404 "$(http "$base/subscriptions/00000000-0000-0000-0000-000000000000")"
check "plans list (seeded)" 200 "$(http "$base/plans")"
check "  3 seeded plans" True "$(json "len(d['items']) >= 3")"
check "create plan" 201 "$(http -XPOST -H 'content-type: application/json' -d '{"code":"smoke","name":"Smoke","amount":1,"currency":"USD","interval":"YEAR","trialDays":0}' "$base/plans")"
check "duplicate plan -> 409" 409 "$(http -XPOST -H 'content-type: application/json' -d '{"code":"smoke","name":"Smoke","amount":1,"currency":"USD","interval":"YEAR","trialDays":0}' "$base/plans")"
check "trace id echoed" "smoke-trace" "$(curl -s -D - -o /dev/null -H 'X-Trace-Id: smoke-trace' "$base/plans" | tr -d '\r' | awk -F': ' 'tolower($1)=="x-trace-id"{print $2}')"
check "trace id echoed on error responses" "err-trace" "$(curl -s -D - -o /dev/null -H 'X-Trace-Id: err-trace' "$base/subscriptions/00000000-0000-0000-0000-000000000000" | tr -d '\r' | awk -F': ' 'tolower($1)=="x-trace-id"{print $2}')"
check "openapi document" 200 "$(http "$base/openapi/v1.json")"
check "  describes /subscriptions/{id}/renew" True "$(json "'/subscriptions/{id}/renew' in d['paths']")"
check "scalar ui (after redirect)" 200 "$(http -L "$base/scalar")"
for _ in $(seq 1 30); do grep -q '/v1/traces' "$out/otlp.log" && grep -q '/v1/metrics' "$out/otlp.log" && break; sleep 0.5; done
check "otlp traces exported by native binary" 1 "$(grep -c '/v1/traces' "$out/otlp.log" | awk '{print ($1>0)?1:0}')"
check "otlp metrics exported by native binary" 1 "$(grep -c '/v1/metrics' "$out/otlp.log" | awk '{print ($1>0)?1:0}')"
if grep -qiE "unhandled|exception" "$out/api.log"; then fail "unexpected exception in API log"; fi
echo "== NATIVE AOT SMOKE TEST PASSED"
