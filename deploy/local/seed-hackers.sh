#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
apply=false
for arg in "$@"; do
  [[ "$arg" != --local-system-id ]] || { echo 'The container identifier cannot be overridden.' >&2; exit 1; }
  if [[ "$arg" == --apply ]]; then apply=true; fi
done
if $apply; then
  [[ -z "${DOCKER_HOST:-}" ]] || { echo "Unset DOCKER_HOST; remote Docker engines are not supported." >&2; exit 1; }
  endpoint="$(docker context inspect --format '{{.Endpoints.docker.Host}}')"
  [[ "$endpoint" == unix:///* ]] || { echo "Only a local Docker socket is supported." >&2; exit 1; }
  # Do not accept a localhost tunnel as evidence of a local database: compare
  # Postgres cluster identity with the running Compose container before writing.
  container="$(docker compose -f "$ROOT/docker-compose.yml" ps -q postgres)"
  [[ -n "$container" ]] || { echo 'Start the local stack with deploy/local/dev.sh first.' >&2; exit 1; }
  [[ "$(docker inspect -f '{{index .Config.Labels "com.docker.compose.project"}}' "$container")" == arctic ]] || exit 1
  [[ "$(docker inspect -f '{{index .Config.Labels "com.docker.compose.service"}}' "$container")" == postgres ]] || exit 1
  system_id="$(docker exec "$container" psql -U arctic -d morganhacks -Atc 'SELECT system_identifier FROM pg_control_system()')"
  [[ "$system_id" =~ ^[0-9]+$ ]] || exit 1
fi
cd "$ROOT"
if $apply; then
  exec dotnet run --project src/atlas/MorganHacks.Seed -- "$@" --local-system-id "$system_id"
else
  exec dotnet run --project src/atlas/MorganHacks.Seed -- "$@"
fi
