#!/usr/bin/env bash
#
# Deploys one environment through main.bicep, in the only order that is safe.
#
#   ./deploy.sh staging                     # what-if: show the diff, change nothing
#   ./deploy.sh staging --apply             # do it
#   ./deploy.sh staging --apply <tag>       # deploy an older tag — this is a rollback
#
# Bicep describes what should exist. This handles the two things that are
# genuinely a sequence rather than a state: running the migration job, and not
# updating any service until it has succeeded.

set -euo pipefail

ENVIRONMENT="${1:-}"
MODE="${2:---what-if}"
export IMAGE_TAG="${3:-$(git rev-parse --short HEAD)}"

if [[ "$ENVIRONMENT" != "staging" && "$ENVIRONMENT" != "prod" ]]; then
    echo "Usage: $0 <staging|prod> [--apply] [tag]" >&2
    exit 1
fi

: "${DB_PASSWORD:?set DB_PASSWORD}"
: "${SUPER_ADMIN_EMAIL:?set SUPER_ADMIN_EMAIL}"
export DB_PASSWORD SUPER_ADMIN_EMAIL
export SENTRY_DSN="${SENTRY_DSN:-}"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOCATION="${LOCATION:-eastus}"
GROUP="rg-morganhacks-${ENVIRONMENT}"
JOB="caj-migrations-${ENVIRONMENT}"
PARAMS="${HERE}/${ENVIRONMENT}.bicepparam"

say() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }

if [[ "$MODE" != "--apply" ]]; then
    say "What-if — nothing will be changed"
    DEPLOY_APPS=true az deployment sub what-if \
        -l "$LOCATION" -f "$HERE/main.bicep" -p "$PARAMS" || true
    echo
    echo "Re-run with --apply to make these changes."
    exit 0
fi

say "1/4  Platform  (Postgres, environment, migration job)"
# Apps deliberately excluded from this pass. Updating them now would put new
# code in front of a schema that has not been migrated yet.
DEPLOY_APPS=false az deployment sub create \
    -l "$LOCATION" -n "arctic-${ENVIRONMENT}-platform" \
    -f "$HERE/main.bicep" -p "$PARAMS" -o none

POSTGRES_HOST="$(az deployment sub show -n "arctic-${ENVIRONMENT}-platform" \
    --query properties.outputs.postgresHost.value -o tsv)"

say "2/4  Schemas"
# The migration runner owns tables, not the schemas they live in — creating a
# schema is a one-time privilege it should not need on every run.
PGPASSWORD="$DB_PASSWORD" psql \
    "host=${POSTGRES_HOST} port=5432 dbname=morganhacks user=arctic sslmode=require" \
    -v ON_ERROR_STOP=1 -qf "$HERE/../local/postgres/01-schemas.sql"
echo "  schemas present"

say "3/4  Migrations"
az containerapp job start -g "$GROUP" -n "$JOB" -o none
echo "  started; waiting"

STATUS=""
for _ in $(seq 1 120); do
    STATUS="$(az containerapp job execution list -g "$GROUP" -n "$JOB" \
        --query "sort_by([], &properties.startTime)[-1].properties.status" -o tsv 2>/dev/null || echo "")"
    [[ "$STATUS" == "Succeeded" || "$STATUS" == "Failed" ]] && break
    sleep 5
done

if [[ "$STATUS" != "Succeeded" ]]; then
    # Fatal on purpose. Deploying anyway would put new code in front of a
    # schema it does not expect, which is worse than not deploying at all.
    echo "  migrations ${STATUS:-timed out} — services NOT deployed" >&2
    echo "  logs: az containerapp job logs show -g $GROUP -n $JOB --container migrations" >&2
    exit 1
fi
echo "  migrations succeeded"

say "4/4  Services"
DEPLOY_APPS=true az deployment sub create \
    -l "$LOCATION" -n "arctic-${ENVIRONMENT}" \
    -f "$HERE/main.bicep" -p "$PARAMS" -o none

FQDN="$(az deployment sub show -n "arctic-${ENVIRONMENT}" \
    --query properties.outputs.harborFqdn.value -o tsv)"

say "Deployed ${IMAGE_TAG}"
echo "  https://${FQDN}/api/health"
curl -sf "https://${FQDN}/api/health" && echo || echo "  (not answering yet — revisions take a moment)"
