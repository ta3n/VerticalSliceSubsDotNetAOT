#!/usr/bin/env bash
# Usage: scripts/measure-startup.sh <port> <probe-path> <command...>
# Starts the command with ASPNETCORE_URLS on <port>, reports time-to-first-successful-response (ms) and RSS (MB).
set -uo pipefail
port=$1; path=$2; shift 2
ASPNETCORE_URLS="http://127.0.0.1:$port" "$@" >/dev/null 2>&1 &
pid=$!
trap 'kill $pid 2>/dev/null' EXIT
start=$(date +%s%N)
for _ in $(seq 1 600); do
  code=$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$port$path" || true)
  [[ $code != 000 ]] && break
  sleep 0.01
done
end=$(date +%s%N)
rss_kb=$(awk '/VmRSS/{print $2}' /proc/$pid/status)
echo "first_response_http=$code startup_ms=$(( (end-start)/1000000 )) rss_mb=$(( rss_kb/1024 ))"
