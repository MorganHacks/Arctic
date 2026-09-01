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

# A subscription-scoped deployment records the location it ran in and refuses
# to run again from another one. Changing region therefore means deleting the
# record first:
#
#   az deployment sub delete -n arctic-<env>-platform
#   az deployment sub delete -n arctic-<env>
LOCATION="${LOCATION:-centralus}"
GROUP="rg-mh-${ENVIRONMENT}"
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

say "1/5  Registry"
# The registry alone. It has to exist and hold the images before the migration
# job can even be created — Container Apps checks that the image is really
# there when the job is defined, not when it runs.
DEPLOY_PLATFORM=false DEPLOY_APPS=false az deployment sub create \
    -l "$LOCATION" -n "arctic-${ENVIRONMENT}-registry" \
    -f "$HERE/main.bicep" -p "$PARAMS" -o none

say "2/5  Images"
# After the platform pass, because the registry has to exist before anything
# can be pushed to it — and before migrations, because the job cannot run an
# image that is not there.
#
# SKIP_PUSH=1 for a rollback: that tag already exists, and rebuilding it would
# produce different bytes from the ones being rolled back to.
if [[ "${SKIP_PUSH:-}" == "1" ]]; then
    echo "  skipped (SKIP_PUSH=1) — deploying tag ${IMAGE_TAG} as it already is"
else
    "$HERE/push-images.sh" "$ENVIRONMENT" "$IMAGE_TAG"
fi

say "3/5  Platform  (Postgres, apps environment, migration job)"
# Apps deliberately excluded. Updating them now would put new code in front of
# a schema that has not been migrated yet.
DEPLOY_APPS=false az deployment sub create \
    -l "$LOCATION" -n "arctic-${ENVIRONMENT}-platform" \
    -f "$HERE/main.bicep" -p "$PARAMS" -o none

say "4/5  Migrations"
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

say "5/5  Services"
# Platform excluded: it already exists, and re-writing the Postgres extension
# configuration while the server is still settling from the last write fails
# with ServerIsBusy. Each pass does one thing.
DEPLOY_PLATFORM=false DEPLOY_APPS=true az deployment sub create \
    -l "$LOCATION" -n "arctic-${ENVIRONMENT}" \
    -f "$HERE/main.bicep" -p "$PARAMS" -o none

FQDN="$(az deployment sub show -n "arctic-${ENVIRONMENT}" \
    --query properties.outputs.harborFqdn.value -o tsv)"

say "Deployed ${IMAGE_TAG}"
echo "  https://${FQDN}/api/health"
curl -sf "https://${FQDN}/api/health" && echo || echo "  (not answering yet — revisions take a moment)"
