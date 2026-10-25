#!/usr/bin/env sh
# Local image registry for kind (https://kind.sigs.k8s.io/docs/user/local-registry/). Idempotent; run after the
# cluster exists (task registry:setup). The same name, localhost:${REGISTRY_PORT}, works on both ends:
#   host  : docker push localhost:5001/<image>      -> published port of the registry container
#   nodes : containerd pulls localhost:5001/<image> -> hosts.toml mirror -> http://kind-registry:5000 on the kind network
# kind node images v0.27+ already read /etc/containerd/certs.d, so no containerdConfigPatches are needed.
set -eu

CLUSTER=${CLUSTER:-kind}
REGISTRY_NAME=${REGISTRY_NAME:-kind-registry}
REGISTRY_PORT=${REGISTRY_PORT:-5001}
REGISTRY_IMAGE=${REGISTRY_IMAGE:-docker.io/library/registry:3}
here=$(cd -- "$(dirname -- "$0")" && pwd)

# 1. Registry container, with its blobs on a named volume so pushed images survive `docker rm`.
if [ "$(docker inspect -f '{{.State.Running}}' "$REGISTRY_NAME" 2>/dev/null || true)" != true ]; then
  docker rm -f "$REGISTRY_NAME" >/dev/null 2>&1 || true
  docker run -d --restart=always --name "$REGISTRY_NAME" \
    -p "127.0.0.1:${REGISTRY_PORT}:5000" \
    -v "${REGISTRY_NAME}-data:/var/lib/registry" \
    -e REGISTRY_STORAGE_DELETE_ENABLED=true \
    "$REGISTRY_IMAGE" >/dev/null
  echo "registry: started $REGISTRY_NAME on localhost:$REGISTRY_PORT"
fi

# 2. Same Docker network as the kind nodes, so they reach it by container name.
if [ "$(docker inspect -f '{{json .NetworkSettings.Networks.kind}}' "$REGISTRY_NAME")" = null ]; then
  docker network connect kind "$REGISTRY_NAME"
  echo "registry: connected to the kind network"
fi

# 3. Alias localhost:${REGISTRY_PORT} to the registry container in every node's containerd.
dir="/etc/containerd/certs.d/localhost:${REGISTRY_PORT}"
for node in $(kind get nodes --name "$CLUSTER"); do
  docker exec "$node" mkdir -p "$dir"
  sed "s/@REGISTRY_NAME@/${REGISTRY_NAME}/" "$here/hosts.toml" | docker exec -i "$node" cp /dev/stdin "$dir/hosts.toml"
  echo "registry: configured containerd on $node"
done

# 4. Advertise it to cluster tooling (KEP-1755).
sed "s/@REGISTRY_PORT@/${REGISTRY_PORT}/" "$here/local-registry-hosting.yaml" \
  | kubectl --context "kind-${CLUSTER}" apply -f -
