// Staging.
//
// Secrets are read from the environment rather than written here, so this file
// is safe in the repository and there is one fewer place a password can be
// committed by accident.
using 'main.bicep'

param environmentName = 'staging'
param location = 'centralus'
param registryName = 'crmharctic'

param imageTag = readEnvironmentVariable('IMAGE_TAG')
param dbPassword = readEnvironmentVariable('DB_PASSWORD')
param superAdminEmail = readEnvironmentVariable('SUPER_ADMIN_EMAIL')
param sentryDsn = readEnvironmentVariable('SENTRY_DSN', '')

param awsRegion = readEnvironmentVariable('AWS_REGION', '')
param awsAccessKeyId = readEnvironmentVariable('AWS_ACCESS_KEY_ID', '')
param awsSecretAccessKey = readEnvironmentVariable('AWS_SECRET_ACCESS_KEY', '')

param googleClientId = readEnvironmentVariable('GOOGLE_CLIENT_ID', '')
param googleClientSecret = readEnvironmentVariable('GOOGLE_CLIENT_SECRET', '')
param googleRedirectUri = readEnvironmentVariable('GOOGLE_REDIRECT_URI', '')

// The portalweb origin. Emailed sign-in links are built from it.
param publicBaseUrl = readEnvironmentVariable('PUBLIC_BASE_URL', '')

param deployPlatform = bool(readEnvironmentVariable('DEPLOY_PLATFORM', 'true'))
param deployApps = bool(readEnvironmentVariable('DEPLOY_APPS', 'true'))
