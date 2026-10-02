// Staging.
//
// Secrets are read from the environment rather than written here, so this file
// is safe in the repository and there is one fewer place a password can be
// committed by accident.
using 'main.bicep'

param environmentName = 'staging'
param location = 'centralus'
// Which registry the images come from.
//
// Default is the shared one, which is what both environments used while they
// lived in the same subscription. An environment in a different subscription
// needs its own: a managed identity cannot be granted AcrPull on a registry in
// another tenant, because role assignments do not cross directories. Set
// REGISTRY_NAME on that environment.
param registryName = readEnvironmentVariable('REGISTRY_NAME', 'crmharctic')

// Where that registry lives. Deliberately not `location`: the registry is
// shared across environments, so it cannot move when one environment does.
param sharedLocation = readEnvironmentVariable('SHARED_LOCATION', 'centralus')

param imageTag = readEnvironmentVariable('IMAGE_TAG')
param dbPassword = readEnvironmentVariable('DB_PASSWORD')
param superAdminEmail = readEnvironmentVariable('SUPER_ADMIN_EMAIL')
param sentryDsn = readEnvironmentVariable('SENTRY_DSN', '')

param awsRegion = readEnvironmentVariable('AWS_REGION', '')
param awsAccessKeyId = readEnvironmentVariable('AWS_ACCESS_KEY_ID', '')
param awsSecretAccessKey = readEnvironmentVariable('AWS_SECRET_ACCESS_KEY', '')

// Empty means SES reports nothing back about a send. See apps.bicep.
param sesConfigurationSet = readEnvironmentVariable('SES_CONFIGURATION_SET', '')

param googleClientId = readEnvironmentVariable('GOOGLE_CLIENT_ID', '')
param googleClientSecret = readEnvironmentVariable('GOOGLE_CLIENT_SECRET', '')
param googleRedirectUri = readEnvironmentVariable('GOOGLE_REDIRECT_URI', '')

// The portalweb origin. Emailed sign-in links are built from it.
param publicBaseUrl = readEnvironmentVariable('PUBLIC_BASE_URL', '')
param consoleBaseUrl = readEnvironmentVariable('CONSOLE_BASE_URL', '')

// The portalforms origin. A sign-in link for a form lands here instead, so the
// session cookie is set on the host the form is actually served from.
param formsBaseUrl = readEnvironmentVariable('FORMS_BASE_URL', '')

// Keep one web-facing replica warm. Set WARM_REPLICAS=0 explicitly to allow
// idle services to sleep and accept cold starts.
// Unset and empty have to mean the same thing here: a GitHub variable that
// does not exist arrives as an empty string, and int('') fails the whole
// deployment rather than the parameter.
param warmReplicas = int(empty(readEnvironmentVariable('WARM_REPLICAS', '1'))
  ? '1'
  : readEnvironmentVariable('WARM_REPLICAS', '1'))

// Keep the mail worker running. Zero lets it sleep until a message is actually
// queued, which only works because apps.bicep adds a scale rule when this is
// zero. Same empty-string handling as warmReplicas above, and for the same
// reason: int('') fails the deployment rather than the parameter.
param larkWarmReplicas = int(empty(readEnvironmentVariable('LARK_WARM_REPLICAS', '1'))
  ? '1'
  : readEnvironmentVariable('LARK_WARM_REPLICAS', '1'))

// Shared secret proving a request reached harbor through one of our front
// ends. Empty means forwarded addresses are never believed, which is a coarser
// rate limit rather than an absent one -- so a missing variable degrades
// safely.
// Whether the applicant portal is served. Empty leaves features.json to decide,
// which is how a flag stays off everywhere until somebody turns it on for one
// environment. Set ENABLE_HACKER_PORTAL_FEATURE on the GitHub environment.
param enableHackerPortalFeature = readEnvironmentVariable('ENABLE_HACKER_PORTAL_FEATURE', '')

param proxySecret = readEnvironmentVariable('PROXY_SHARED_SECRET', '')

param deployPlatform = bool(readEnvironmentVariable('DEPLOY_PLATFORM', 'true'))
param deployApps = bool(readEnvironmentVariable('DEPLOY_APPS', 'true'))
