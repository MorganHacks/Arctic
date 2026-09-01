#!/usr/bin/env bash
#
# Builds the four images and pushes them to the shared registry.
#
#   ./push-images.sh staging $(git rev-parse --short HEAD)
#
# Tagged by commit rather than by :latest, so every deployed thing can be
# traced back to what produced it. A rollback is then re-deploying a tag that
# already exists rather than rebuilding and hoping.

set -euo pipefail

ENVIRONMENT="${1:-}"
TAG="${2:-$(git rev-parse --short HEAD)}"
REGISTRY="${REGISTRY:-morganhacksacr}"

if [[ -z "$ENVIRONMENT" ]]; then
    echo "Usage: $0 <staging|prod> [tag]" >&2
    exit 1
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

az acr login -n "$REGISTRY"
LOGIN_SERVER="$(az acr show -n "$REGISTRY" --query loginServer -o tsv)"

build_and_push () {
    local name="$1" dockerfile="$2"
    local image="${LOGIN_SERVER}/${name}:${TAG}"

    printf '\n\033[1m==> %s\033[0m\n' "$name"

    # linux/amd64 explicitly. Container Apps runs amd64, and a Mac builds
    # arm64 by default — an image that runs perfectly on the laptop that built
    # it and crash-loops in the cloud is a genuinely confusing hour.
    docker build --platform linux/amd64 -f "$dockerfile" -t "$image" .
    docker push "$image"
    echo "  pushed $image"
}

build_and_push atlas      src/atlas/Dockerfile
build_and_push harbor     src/harbor/Dockerfile
build_and_push lark       src/lark/Dockerfile
build_and_push migrations src/atlas/Dockerfile.migrations

printf '\n  All four pushed at tag %s\n' "$TAG"
