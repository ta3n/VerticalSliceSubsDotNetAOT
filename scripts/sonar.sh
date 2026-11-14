#!/usr/bin/env bash
# Local SonarQube workflow (deploy/sonarqube/compose.yaml). Usually run through the Taskfile (task sonar:*).
#   scripts/sonar.sh up       start SonarQube, wait until it is UP, set up admin password + project + analysis token
#   scripts/sonar.sh scan     build + unit/integration tests with coverage (OpenCover) + analysis, then wait for the result
#   scripts/sonar.sh report   quality gate, measures and open issues of the last analysis
#   scripts/sonar.sh down     stop (volumes kept)          scripts/sonar.sh purge   stop and delete all SonarQube data
# Credentials are generated on first `up` and stored in .sonar/ (git-ignored): admin-password, token.
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
cd "$root"

SONAR_URL=${SONAR_URL:-http://localhost:${SONAR_PORT:-9000}}
PROJECT_KEY=${SONAR_PROJECT_KEY:-subscription-management}
PROJECT_NAME=${SONAR_PROJECT_NAME:-Subscription Management}
COMPOSE=(docker compose -f deploy/sonarqube/compose.yaml)
state=.sonar
mkdir -p "$state"

api() {   # api METHOD PATH [curl args...]  — as admin (project setup), or with the analysis token for read-backs
  local method=$1 path=$2; shift 2
  curl -fsS -X "$method" -u "admin:$(cat "$state/admin-password")" "$SONAR_URL$path" "$@"
}

wait_up() {
  echo -n "waiting for SonarQube at $SONAR_URL "
  for _ in $(seq 1 120); do
    status=$(curl -fsS "$SONAR_URL/api/system/status" 2>/dev/null | python3 -c 'import json,sys; print(json.load(sys.stdin)["status"])' 2>/dev/null || true)
    [[ $status == UP ]] && { echo " UP"; return 0; }
    echo -n "."; sleep 5
  done
  echo; echo "SonarQube did not come up; see: ${COMPOSE[*]} logs sonarqube" >&2; return 1
}

init() {
  # 1. Replace the default admin/admin (SonarQube forces a change) with a generated password.
  if [[ ! -s $state/admin-password ]]; then
    pw="$(python3 -c 'import secrets,string; a=string.ascii_letters+string.digits; print("Sq-"+"".join(secrets.choice(a) for _ in range(20))+"!9")')"
    curl -fsS -u admin:admin -X POST "$SONAR_URL/api/users/change_password" \
      --data-urlencode login=admin --data-urlencode previousPassword=admin --data-urlencode "password=$pw" >/dev/null
    printf '%s' "$pw" > "$state/admin-password"; chmod 600 "$state/admin-password"
    echo "admin password set (stored in $state/admin-password)"
  fi
  # 2. Project.
  if ! api GET "/api/projects/search?projects=$PROJECT_KEY" | grep -q "\"key\":\"$PROJECT_KEY\""; then
    api POST /api/projects/create --data-urlencode "project=$PROJECT_KEY" --data-urlencode "name=$PROJECT_NAME" >/dev/null
    echo "project $PROJECT_KEY created"
  fi
  # 3. Project analysis token for the scanner.
  if [[ ! -s $state/token ]]; then
    token=$(curl -fsS -u "admin:$(cat "$state/admin-password")" -X POST "$SONAR_URL/api/user_tokens/generate" \
      --data-urlencode "name=local-scan-$(date +%s)" --data-urlencode type=PROJECT_ANALYSIS_TOKEN \
      --data-urlencode "projectKey=$PROJECT_KEY" | python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])')
    printf '%s' "$token" > "$state/token"; chmod 600 "$state/token"
    echo "analysis token created (stored in $state/token)"
  fi
}

scan() {
  [[ -s $state/token ]] || { echo "run 'scripts/sonar.sh up' first" >&2; exit 1; }
  dotnet tool restore >/dev/null
  rm -rf artifacts/coverage
  dotnet sonarscanner begin /k:"$PROJECT_KEY" /n:"$PROJECT_NAME" /d:sonar.host.url="$SONAR_URL" \
    /d:sonar.token="$(cat "$state/token")" \
    /d:sonar.cs.opencover.reportsPaths="artifacts/coverage/**/coverage.opencover.xml" \
    /d:sonar.exclusions="graphify-out/**,artifacts/**,docs/**,**/bin/**,**/obj/**,.serena/**,.idea/**,.vs/**,src/AppHost/observability/grafana/dashboards/**" \
    /d:sonar.coverage.exclusions="tests/**,src/AppHost/**,scripts/**,deploy/**,**/*.js,**/*.py" \
    /d:sonar.cpd.exclusions="tests/**" \
    `# kubernetes:S5332 (clear-text protocol): in-cluster service-to-service URLs of the local kind platform (Prometheus,` \
    `# Alloy). TLS there would need a service mesh / cert-manager, out of scope for this local setup.` \
    /d:sonar.issue.ignore.multicriteria=k8shttp \
    /d:sonar.issue.ignore.multicriteria.k8shttp.ruleKey=kubernetes:S5332 \
    /d:sonar.issue.ignore.multicriteria.k8shttp.resourceKey="deploy/**" \
    /d:sonar.qualitygate.wait=false
  dotnet build SubscriptionManagement.slnx --no-incremental
  dotnet test SubscriptionManagement.slnx --no-build --results-directory artifacts/coverage \
    --collect:"XPlat Code Coverage" --settings tests/coverage.runsettings
  dotnet sonarscanner end /d:sonar.token="$(cat "$state/token")"
  wait_for_task
  report
}

wait_for_task() {
  local task_id status
  task_id=$(sed -n 's/^ceTaskId=//p' .sonarqube/out/.sonar/report-task.txt)
  echo -n "waiting for the server to process the analysis "
  for _ in $(seq 1 120); do
    status=$(api GET "/api/ce/task?id=$task_id" | python3 -c 'import json,sys; print(json.load(sys.stdin)["task"]["status"])')
    case $status in SUCCESS) echo " done"; return 0 ;; FAILED|CANCELED) echo " $status" >&2; return 1 ;; esac
    echo -n "."; sleep 3
  done
}

report() {
  python3 -I scripts/sonar_report.py "$SONAR_URL" "$PROJECT_KEY" "admin:$(cat "$state/admin-password")"
}

case ${1:-} in
  up)     "${COMPOSE[@]}" up -d; wait_up; init; echo "SonarQube: $SONAR_URL  (admin / see $state/admin-password)" ;;
  scan)   scan ;;
  report) report ;;
  down)   "${COMPOSE[@]}" down ;;
  purge)  "${COMPOSE[@]}" down -v; rm -f "$state/admin-password" "$state/token" ;;
  *)      sed -n '2,7p' "$0"; exit 2 ;;
esac
