#!/usr/bin/env bash
#
# Deploys one environment, in the only order that is safe.
#
#   ./deploy.sh staging                        # what-if: show the diff, change nothing
#   ./deploy.sh staging --apply                # actually do it
#   ./deploy.sh staging --apply <tag>          # deploy a specific tag (this is a rollback)
#
# The templates describe what should exist. This handles the parts that are
# genuinely imperative — running the migration job, and waiting for it — plus
# the ordering that keeps new code from ever meeting an old schema.

set -euo pipefail

ENVIRONMENT="${1:-}"
MODE="${2:---what-if}"
TAG="${3:-$(git rev-parse --short HEAD)}"

if [[ "$ENVIRONMENT" != "staging" && "$ENVIRONMENT" != "prod" ]]; then
    echo "Usage: $0 <staging|prod> [--apply] [tag]" >&2
    exit 1
fi

GROUP="morganhacks-${ENVIRONMENT}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

: "${DB_PASSWORD:?set DB_PASSWORD}"
: "${SUPER_ADMIN_EMAIL:?set SUPER_ADMIN_EMAIL}"
REGISTRY="${REGISTRY:-morganhacksacr}"
SENTRY_DSN="${SENTRY_DSN:-}"

say() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }

COMMON=(
    --parameters environmentName="$ENVIRONMENT"
    --parameters imageTag="$TAG"
    --parameters registryName="$REGISTRY"
    --parameters dbPassword="$DB_PASSWORD"
    --parameters sentryDsn="$SENTRY_DSN"
)

if [[ "$MODE" != "--apply" ]]; then
    say "What-if — nothing will be changed"
    echo "Platform:"
    az deployment group what-if -g "$GROUP" -f "$HERE/platform.bicep" \
        "${COMMON[@]}" --parameters superAdminEmail="$SUPER_ADMIN_EMAIL" || true
    echo
    echo "Apps:"
    az deployment group what-if -g "$GROUP" -f "$HERE/apps.bicep" "${COMMON[@]}" || true
    echo
    echo "Re-run with --apply to make these changes."
    exit 0
fi

say "1/4  Platform  (Postgres, environment, migration job)"
az deployment group create -g "$GROUP" -f "$HERE/platform.bicep" \
    "${COMMON[@]}" --parameters superAdminEmail="$SUPER_ADMIN_EMAIL" -o none

say "2/4  Schemas"
# The migration runner owns tables. It does not own the schemas they live in,
# because creating a schema is a one-time privilege the runner should not need
# every time it runs.
POSTGRES_HOST="$(az deployment group show -g "$GROUP" -n platform \
    --query properties.outputs.postgresHost.value -o tsv)"
PGPASSWORD="$DB_PASSWORD" psql \
    "host=${POSTGRES_HOST} port=5432 dbname=morganhacks user=arctic sslmode=require" \
    -v ON_ERROR_STOP=1 -qf "$HERE/../local/postgres/01-schemas.sql"
echo "  schemas present"

say "3/4  Migrations"
az containerapp job start -g "$GROUP" -n migrations -o none
echo "  started; waiting"

STATUS=""
for _ in $(seq 1 120); do
    STATUS="$(az containerapp job execution list -g "$GROUP" -n migrations \
        --query "sort_by([], &properties.startTime)[-1].properties.status" -o tsv 2>/dev/null || echo "")"
    [[ "$STATUS" == "Succeeded" || "$STATUS" == "Failed" ]] && break
    sleep 5
done

if [[ "$STATUS" != "Succeeded" ]]; then
    # Deliberately fatal. Deploying the services anyway would put new code in
    # front of a schema it does not expect, which is worse than not deploying.
    echo "  migrations ${STATUS:-timed out} — services NOT deployed" >&2
    echo "  logs: az containerapp job logs show -g $GROUP -n migrations" >&2
    exit 1
fi
echo "  migrations succeeded"

say "4/4  Services"
az deployment group create -g "$GROUP" -f "$HERE/apps.bicep" "${COMMON[@]}" -o none

FQDN="$(az deployment group show -g "$GROUP" -n apps \
    --query properties.outputs.harborFqdn.value -o tsv)"

say "Deployed ${TAG}"
echo "  https://${FQDN}/api/health"
curl -sf "https://${FQDN}/api/health" && echo || echo "  (not answering yet — revisions take a moment)"
