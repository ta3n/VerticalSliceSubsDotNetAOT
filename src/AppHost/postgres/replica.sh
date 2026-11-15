#!/usr/bin/env bash
# Entrypoint of a PostgreSQL streaming replica (hot standby), on the official postgres image.
# First start: clone the primary with pg_basebackup (-R writes standby.signal and primary_conninfo). Later starts: the data
# volume already holds a standby, which just resumes streaming. Environment: PRIMARY_HOST, PRIMARY_PORT, PGPASSWORD
# (also what the walreceiver authenticates with) and POSTGRES_ARGS (the primary's `-c` settings: a standby must allow at
# least as many connections as the primary).
set -euo pipefail

PGDATA=${PGDATA:-/var/lib/postgresql/data}
export PGDATA

if [[ ! -s "$PGDATA/PG_VERSION" ]]; then
  until pg_isready -h "$PRIMARY_HOST" -p "$PRIMARY_PORT" -U postgres -q; do
    echo "waiting for the primary at $PRIMARY_HOST:$PRIMARY_PORT"
    sleep 1
  done
  mkdir -p "$PGDATA"
  chown postgres:postgres "$PGDATA"
  chmod 700 "$PGDATA"
  echo "cloning the primary"
  gosu postgres pg_basebackup -h "$PRIMARY_HOST" -p "$PRIMARY_PORT" -U postgres -D "$PGDATA" -R -X stream --checkpoint=fast
fi

# shellcheck disable=SC2086 # POSTGRES_ARGS is a word list on purpose
exec docker-entrypoint.sh postgres -c hot_standby=on ${POSTGRES_ARGS:-}
