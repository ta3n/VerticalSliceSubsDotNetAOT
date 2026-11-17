#!/usr/bin/env sh
# One release file pins all six images, shared by plain Helm and the Argo CD chart publisher.
set -eu
registry=$1 tag=$2 api_repo=$3 migrator_repo=$4 frontend_repo=$5
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
emit_image() {
  repository=$1 indent=$2
  case "$registry/$repository:$tag" in
    *[!a-zA-Z0-9._:/-]*) echo "Invalid image reference" >&2; exit 1 ;;
  esac
  digest=$("$script_dir/digest.sh" "$registry" "$repository" "$tag")
  { [ "${#digest}" -eq 71 ] && printf '%s' "$digest" | grep -Eq '^sha256:[a-f0-9]{64}$'; } || { echo "Missing/invalid digest for $repository:$tag" >&2; exit 1; }
  printf '%srepository: "%s/%s"\n%stag: "%s"\n%sdigest: "%s"\n' "$indent" "$registry" "$repository" "$indent" "$tag" "$indent" "$digest"
}
printf 'image:\n'
emit_image "$api_repo" '  '
printf 'migrator:\n  image:\n'
emit_image "$migrator_repo" '    '
printf 'frontend:\n  image:\n'
emit_image "$frontend_repo" '    '
printf 's2s:\n'
for component in billing payments relay; do
  printf '  %s:\n    image:\n' "$component"
  emit_image "subscription-management-$component" '      '
done
