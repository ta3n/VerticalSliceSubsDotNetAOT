# Sourced by the test scripts. Loads KEY=VALUE lines from $ENV_FILE (default: scripts/.env) without overriding variables
# that are already set, so `VUS=200 scripts/load-test.sh` still beats the file. ENV_FILE=none skips loading.
# Format: one KEY=VALUE per line, optional `export `, optional surrounding quotes, comments on their own line.
load_env() {
  local file=${ENV_FILE:-$(dirname -- "${BASH_SOURCE[0]}")/.env} line key value
  [[ -f $file ]] || return 0
  while IFS= read -r line || [[ -n $line ]]; do
    line=${line%$'\r'}
    [[ $line =~ ^[[:space:]]*(export[[:space:]]+)?([A-Za-z_][A-Za-z0-9_]*)=(.*)$ ]] || continue
    key=${BASH_REMATCH[2]} value=${BASH_REMATCH[3]}
    if [[ $value =~ ^\"(.*)\"$ || $value =~ ^\'(.*)\'$ ]]; then value=${BASH_REMATCH[1]}; fi
    [[ -n ${!key+x} ]] || export "$key=$value"
  done <"$file"
  echo "env: $file" >&2
}
load_env

# Container user for the k6/Locust runs: rootless podman maps root to the invoking user, but docker's root is real root,
# so with docker the containers run as the invoking user and artifacts/ stays writable.
container_user() { if [[ ${CONTAINER_ENGINE:-podman} == docker ]]; then echo "$(id -u):$(id -g)"; else echo 0:0; fi; }
