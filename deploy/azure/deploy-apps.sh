#!/usr/bin/env bash
#
# Deploys the three services and the migration job.
#
#   ./deploy-apps.sh staging <tag>
#
# Migrations run first, as a job that must succeed before anything else is
# updated. An API that migrates on startup means every replica racing to alter
# one schema.

set -euo pipefail

ENVIRONMENT="${1:-}"
TAG="${2:-}"
REGISTRY="${REGISTRY:-morganhacksacr}"

if [[ -z "$ENVIRONMENT" || -z "$TAG" ]]; then
    echo "Usage: $0 <staging|prod> <tag>" >&2
    exit 1
fi

GROUP="morganhacks-${ENVIRONMENT}"
ENV_NAME="morganhacks-${ENVIRONMENT}-env"
LOGIN_SERVER="$(az acr show -n "$REGISTRY" --query loginServer -o tsv)"

: "${ARCTIC_DB:?set ARCTIC_DB to the Postgres connection string}"
: "${SUPER_ADMIN_EMAIL:?set SUPER_ADMIN_EMAIL}"

say() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }

# The registry credential every app uses to pull. Admin user rather than a
# managed identity for now; identity is the better answer and is a change to
# this one function later.
REG_USER="$(az acr credential show -n "$REGISTRY" --query username -o tsv)"
REG_PASS="$(az acr credential show -n "$REGISTRY" --query 'passwords[0].value' -o tsv)"

say "Migrations"
if ! az containerapp job show -g "$GROUP" -n migrations -o none 2>/dev/null; then
    az containerapp job create \
        -g "$GROUP" -n migrations --environment "$ENV_NAME" \
        --trigger-type Manual --replica-timeout 600 --replica-retry-limit 1 \
        --image "${LOGIN_SERVER}/migrations:${TAG}" \
        --registry-server "$LOGIN_SERVER" \
        --registry-username "$REG_USER" --registry-password "$REG_PASS" \
        --cpu 0.5 --memory 1Gi \
        --secrets "db=${ARCTIC_DB}" \
        --env-vars "ARCTIC_DB=secretref:db" \
                   "ARCTIC_SUPER_ADMIN_EMAIL=${SUPER_ADMIN_EMAIL}" -o none
else
    az containerapp job update \
        -g "$GROUP" -n migrations \
        --image "${LOGIN_SERVER}/migrations:${TAG}" -o none
fi

echo "  running, and waiting for it to finish"
az containerapp job start -g "$GROUP" -n migrations -o none

# Nothing else deploys until the schema is in the state the new code expects.
for _ in $(seq 1 60); do
    STATUS="$(az containerapp job execution list -g "$GROUP" -n migrations \
        --query "sort_by([], &properties.startTime)[-1].properties.status" -o tsv 2>/dev/null || echo "")"
    [[ "$STATUS" == "Succeeded" || "$STATUS" == "Failed" ]] && break
    sleep 5
done

if [[ "${STATUS:-}" != "Succeeded" ]]; then
    echo "  migrations did not succeed (${STATUS:-unknown}); nothing else deployed" >&2
    exit 1
fi
echo "  migrations succeeded"

deploy_app () {
    local name="$1" ingress="$2"
    shift 2

    say "$name"
    if az containerapp show -g "$GROUP" -n "$name" -o none 2>/dev/null; then
        az containerapp update -g "$GROUP" -n "$name" \
            --image "${LOGIN_SERVER}/${name}:${TAG}" "$@" -o none
        echo "  updated to ${TAG}"
    else
        local ingress_args=()
        case "$ingress" in
            external) ingress_args=(--ingress external --target-port 8080) ;;
            internal) ingress_args=(--ingress internal --target-port 8080) ;;
            none)     ingress_args=() ;;
        esac

        az containerapp create \
            -g "$GROUP" -n "$name" --environment "$ENV_NAME" \
            --image "${LOGIN_SERVER}/${name}:${TAG}" \
            --registry-server "$LOGIN_SERVER" \
            --registry-username "$REG_USER" --registry-password "$REG_PASS" \
            "${ingress_args[@]}" "$@" -o none
        echo "  created at ${TAG}"
    fi
}

# atlas is internal. Harbor is the only path to the API, and while atlas
# validates its own sessions and permissions rather than trusting the gateway,
# there is no reason to also publish it.
deploy_app atlas internal \
    --min-replicas 1 --max-replicas 3 --cpu 0.5 --memory 1Gi \
    --secrets "db=${ARCTIC_DB}" \
    --env-vars "ARCTIC_DB=secretref:db" "ASPNETCORE_URLS=http://+:8080"

# lark takes no inbound traffic at all. min-replicas 1 rather than 0: it has no
# ingress, so nothing would ever wake it, and a queue with no worker is a queue
# that silently stops sending.
deploy_app lark none \
    --min-replicas 1 --max-replicas 1 --cpu 0.5 --memory 1Gi \
    --secrets "db=${ARCTIC_DB}" \
    --env-vars "ARCTIC_DB=secretref:db"

# harbor is the only thing exposed.
deploy_app harbor external \
    --min-replicas 1 --max-replicas 3 --cpu 0.5 --memory 1Gi \
    --env-vars "ASPNETCORE_URLS=http://+:8080" \
               "ReverseProxy__Clusters__atlas__Destinations__primary__Address=http://atlas/"

say "Done"
FQDN="$(az containerapp show -g "$GROUP" -n harbor --query properties.configuration.ingress.fqdn -o tsv)"
cat <<SUMMARY

  https://${FQDN}/api/health

  Still to configure, and both matter before this carries real traffic:

    Network__KnownNetworks   Container Apps sits in front of harbor, so until
                             this names it, every per-IP rate limit shares one
                             bucket for the whole internet. See
                             docs/architecture/deployments.md.

    Sentry__Dsn              Off until set.

SUMMARY
