#!/usr/bin/env bash
#
# Provisions one environment on Azure Container Apps.
#
# Safe to re-run. Every step either creates a resource or confirms the one that
# is already there, so this doubles as the recovery procedure: if staging is
# ever lost, this rebuilds it. That is the whole reason it exists as a script
# rather than as a sequence of clicks nobody wrote down.
#
#   ./provision.sh staging
#   ./provision.sh prod
#
# Requires: az login, and a subscription selected.

set -euo pipefail

ENVIRONMENT="${1:-}"
if [[ "$ENVIRONMENT" != "staging" && "$ENVIRONMENT" != "prod" ]]; then
    echo "Usage: $0 <staging|prod>" >&2
    exit 1
fi

LOCATION="${LOCATION:-eastus}"

# One registry for every environment, in its own group. Images are built once
# and promoted by digest, so staging and production run the identical bytes —
# a rebuild for production would be a different image that happens to come
# from the same commit.
SHARED_GROUP="morganhacks-shared"
REGISTRY="${REGISTRY:-morganhacksacr}"

GROUP="morganhacks-${ENVIRONMENT}"
ENV_NAME="morganhacks-${ENVIRONMENT}-env"
LOGS="morganhacks-${ENVIRONMENT}-logs"
POSTGRES="morganhacks-${ENVIRONMENT}-pg"
DB_NAME="morganhacks"
DB_USER="arctic"

# Postgres 18 locally and in the tests. Override if the region has not caught
# up — but keep local and deployed on the same major version. A difference here
# is one that only shows up under load, in production, at the worst time.
PG_VERSION="${PG_VERSION:-17}"

say() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }

say "Environment: ${ENVIRONMENT}   Region: ${LOCATION}"
az account show --query "{subscription:name, user:user.name}" -o tsv

say "Providers"
for provider in Microsoft.App Microsoft.ContainerRegistry Microsoft.DBforPostgreSQL \
                Microsoft.OperationalInsights; do
    state=$(az provider show -n "$provider" --query registrationState -o tsv 2>/dev/null || echo "NotRegistered")
    if [[ "$state" != "Registered" ]]; then
        echo "  registering $provider (this takes a minute)"
        az provider register -n "$provider" --wait
    else
        echo "  $provider already registered"
    fi
done

say "Resource groups"
az group create -n "$SHARED_GROUP" -l "$LOCATION" -o none
az group create -n "$GROUP" -l "$LOCATION" -o none
echo "  $SHARED_GROUP, $GROUP"

say "Container registry"
if ! az acr show -n "$REGISTRY" -g "$SHARED_GROUP" -o none 2>/dev/null; then
    # Basic is enough: this holds a handful of small images, not a public feed.
    az acr create -n "$REGISTRY" -g "$SHARED_GROUP" --sku Basic -o none
    echo "  created $REGISTRY"
else
    echo "  $REGISTRY already exists"
fi

say "Log workspace"
if ! az monitor log-analytics workspace show -g "$GROUP" -n "$LOGS" -o none 2>/dev/null; then
    az monitor log-analytics workspace create -g "$GROUP" -n "$LOGS" -o none
    echo "  created $LOGS"
else
    echo "  $LOGS already exists"
fi

WORKSPACE_ID=$(az monitor log-analytics workspace show -g "$GROUP" -n "$LOGS" \
    --query customerId -o tsv)
WORKSPACE_KEY=$(az monitor log-analytics workspace get-shared-keys -g "$GROUP" -n "$LOGS" \
    --query primarySharedKey -o tsv)

say "Container Apps environment"
if ! az containerapp env show -g "$GROUP" -n "$ENV_NAME" -o none 2>/dev/null; then
    az containerapp env create -g "$GROUP" -n "$ENV_NAME" -l "$LOCATION" \
        --logs-workspace-id "$WORKSPACE_ID" --logs-workspace-key "$WORKSPACE_KEY" -o none
    echo "  created $ENV_NAME"
else
    echo "  $ENV_NAME already exists"
fi

say "Postgres"
if ! az postgres flexible-server show -g "$GROUP" -n "$POSTGRES" -o none 2>/dev/null; then
    if [[ -z "${DB_PASSWORD:-}" ]]; then
        echo "  DB_PASSWORD is not set." >&2
        echo "  Generate one and keep it somewhere real:" >&2
        echo "    export DB_PASSWORD=\$(openssl rand -base64 32 | tr -d '/+=' | head -c 32)" >&2
        exit 1
    fi

    # Burstable B1ms. The plan is explicit that managed Postgres is worth
    # paying for and that self-hosting is the one mistake that is permanent —
    # this is the smallest managed tier, not the cheapest possible database.
    az postgres flexible-server create \
        -g "$GROUP" -n "$POSTGRES" -l "$LOCATION" \
        --version "$PG_VERSION" \
        --tier Burstable --sku-name Standard_B1ms \
        --storage-size 32 \
        --admin-user "$DB_USER" --admin-password "$DB_PASSWORD" \
        --database-name "$DB_NAME" \
        --public-access 0.0.0.0 \
        --yes -o none
    echo "  created $POSTGRES"
else
    echo "  $POSTGRES already exists (password not changed)"
fi

# Backups are the point of managed Postgres, so say what they are rather than
# inheriting a default nobody checked. An untested backup is a belief.
az postgres flexible-server update -g "$GROUP" -n "$POSTGRES" \
    --backup-retention 14 -o none
echo "  backup retention: 14 days"

say "Schemas"
echo "  The migration runner owns tables, but not the schemas they live in."
echo "  Run once against the new server:"
echo
echo "    psql \"\$ARCTIC_DB\" -f deploy/local/postgres/01-schemas.sql"

say "Done"
cat <<SUMMARY

  Registry     ${REGISTRY}.azurecr.io
  Environment  ${ENV_NAME}
  Postgres     ${POSTGRES}.postgres.database.azure.com

  Next:
    1. Create the schemas (above).
    2. Push images:      ./deploy/azure/push-images.sh ${ENVIRONMENT} <tag>
    3. Deploy apps:      ./deploy/azure/deploy-apps.sh ${ENVIRONMENT} <tag>

  Nothing here is reachable from the internet yet. deploy-apps.sh is what gives
  harbor an ingress, and it keeps atlas and lark internal.

SUMMARY
