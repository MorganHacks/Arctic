// Production.
//
// Secrets are read from the environment rather than written here, so this file
// is safe in the repository and there is one fewer place a password can be
// committed by accident.
using 'main.bicep'

param environmentName = 'prod'
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

// The portalforms origin. A sign-in link for a form lands here instead, so the
// session cookie is set on the host the form is actually served from.
param formsBaseUrl = readEnvironmentVariable('FORMS_BASE_URL', '')

// Web-facing replicas kept warm. Zero lets them sleep, which is most of the
// reason this environment is cheap; set WARM_REPLICAS=1 on the GitHub
// environment for the weeks registration is open, and unset it afterwards.
// Unset and empty have to mean the same thing here: a GitHub variable that
// does not exist arrives as an empty string, and int('') fails the whole
// deployment rather than the parameter.
param warmReplicas = int(empty(readEnvironmentVariable('WARM_REPLICAS', '0'))
  ? '0'
  : readEnvironmentVariable('WARM_REPLICAS', '0'))

// Shared secret proving a request reached harbor through one of our front
// ends. Empty means forwarded addresses are never believed, which is a coarser
// rate limit rather than an absent one -- so a missing variable degrades
// safely.
param proxySecret = readEnvironmentVariable('PROXY_SHARED_SECRET', '')

param deployPlatform = bool(readEnvironmentVariable('DEPLOY_PLATFORM', 'true'))
param deployApps = bool(readEnvironmentVariable('DEPLOY_APPS', 'true'))
