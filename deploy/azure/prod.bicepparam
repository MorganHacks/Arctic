// Production.
//
// Secrets are read from the environment rather than written here, so this file
// is safe in the repository and there is one fewer place a password can be
// committed by accident.
using 'main.bicep'

param environmentName = 'prod'
param location = 'eastus'
param registryName = 'crmorganhacks'

param imageTag = readEnvironmentVariable('IMAGE_TAG')
param dbPassword = readEnvironmentVariable('DB_PASSWORD')
param superAdminEmail = readEnvironmentVariable('SUPER_ADMIN_EMAIL')
param sentryDsn = readEnvironmentVariable('SENTRY_DSN', '')

param deployApps = bool(readEnvironmentVariable('DEPLOY_APPS', 'true'))
