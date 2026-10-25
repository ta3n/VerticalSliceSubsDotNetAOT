#!/usr/bin/env sh
# Prints the registry's content digest for <repository>:<tag>, e.g.
#   deploy/registry/digest.sh localhost:5001 subscription-management dev   ->  sha256:...
# The digest is the one the registry serves (image index or manifest), i.e. exactly what the nodes will pull.
set -eu
registry=$1 repository=$2 tag=$3
curl -fsSI \
  -H 'Accept: application/vnd.oci.image.index.v1+json' \
  -H 'Accept: application/vnd.docker.distribution.manifest.list.v2+json' \
  -H 'Accept: application/vnd.oci.image.manifest.v1+json' \
  -H 'Accept: application/vnd.docker.distribution.manifest.v2+json' \
  "http://${registry}/v2/${repository}/manifests/${tag}" \
  | tr -d '\r' | awk -F': ' 'tolower($1) == "docker-content-digest" { print $2 }'
