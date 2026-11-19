#!/usr/bin/env sh
set -eu
registry=$1 path=$2
shift 2

host=${registry%%:*}
case "$host" in
  localhost|127.0.0.1)
    suffix=${registry#"$host"}
    case "${suffix#:}" in
      *[!0-9]*) echo 'Invalid loopback registry port' >&2; exit 1 ;;
    esac
    scheme=http
    set -- --noproxy '*' "$@"
    ;;
  *) scheme=https ;;
esac

exec curl --proto "=$scheme" --proto-default "$scheme" --no-insecure "$@" -- "$registry/${path#/}"
