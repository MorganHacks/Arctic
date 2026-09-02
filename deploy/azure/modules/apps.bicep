// The three services.
//
// Deployed after platform.bicep and after the migration job has succeeded, so
// nothing here ever runs against a schema it does not expect.

targetScope = 'resourceGroup'

@allowed(['staging', 'prod'])
param environmentName string

param location string = resourceGroup().location

@description('Commit sha. Never "latest" — a rollback has to be a tag that already exists.')
param imageTag string

param registryName string
param registryResourceGroup string = 'rg-mh-shared'

@secure()
param dbPassword string

param dbAdminUser string = 'arctic'
param dbName string = 'morganhacks'

@secure()
@description('Empty disables error reporting, which is what lets this run with no accounts.')
param sentryDsn string = ''

@description('''
Region for SES. Empty means lark runs but sends nothing and says so, rather
than crash-looping on a missing variable — which is what it used to do.
''')
param awsRegion string = ''

@secure()
param awsAccessKeyId string = ''

@secure()
param awsSecretAccessKey string = ''

@description('Google OAuth client id. Empty leaves organizer sign-in answering 503.')
param googleClientId string = ''

@secure()
param googleClientSecret string = ''

@description('''
Must match, character for character, the URI registered with Google — and it is
the origin the *browser* lands on, which is the admin app rather than harbor,
because the admin app proxies the API from its own origin.
''')
param googleRedirectUri string = ''

@description('''
The origin applicants reach the portal on — portalweb, not harbor.

Emailed sign-in links are built from this, and the link both sets the session
cookie and lands the person on /portal, so it has to be the host they will
actually be browsing: a cookie set on the API's own origin is one the portal is
never sent. Empty falls back to http://localhost:3000 in atlas, which in a
deployed environment means every link points at a machine nobody is running.
''')
param publicBaseUrl string = ''

@description('Resource id of the identity that pulls images.')
param pullIdentityId string

@description('''
Client id of that same identity, which is what the Azure SDK inside atlas needs
to authenticate as it. A container app with a user-assigned identity attached
still defaults to the system-assigned one, which holds no roles — so leaving
this empty makes every resume upload fail as if the role assignment were
missing.
''')
param pullIdentityClientId string

param tags object = {}

var suffix = 'mh-${environmentName}'

// The same expression platform.bicep builds the account from. Duplicated
// rather than passed, for the same reason the Postgres server is looked up by
// name here: apps.bicep is deployed on its own, after platform, and must not
// depend on that deployment's outputs still being available.
var resumeStorageAccount = 'stmh${environmentName}${uniqueString(subscription().subscriptionId, environmentName)}'

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
  scope: resourceGroup(registryResourceGroup)
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: 'cae-${suffix}'
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' existing = {
  name: 'psql-${suffix}'
}

var connectionString = 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=${dbName};Username=${dbAdminUser};Password=${dbPassword};SSL Mode=Require;Trust Server Certificate=true'

var registryConfig = [
  {
    server: registry.properties.loginServer
    identity: pullIdentityId
  }
]

// Attached to every app and to the job, because a registry credential that is
// an identity has to be an identity the resource actually holds.
var pullIdentityConfig = {
  type: 'UserAssigned'
  userAssignedIdentities: {
    '${pullIdentityId}': {}
  }
}

var dbSecret = {
  name: 'db-connection'
  value: connectionString
}

// Container Apps rejects a secret with an empty value outright, so "Sentry is
// off until a DSN is set" has to mean the secret is absent rather than blank.
// Otherwise the thing that lets this run with no accounts is the thing that
// stops it deploying.
var hasSentry = !empty(sentryDsn)

var sentrySecrets = hasSentry ? [
  {
    name: 'sentry-dsn'
    value: sentryDsn
  }
] : []

// Same shape as Sentry: absent rather than blank, because Container Apps
// rejects a secret with an empty value.
var hasAws = !empty(awsRegion) && !empty(awsAccessKeyId)

var awsSecrets = hasAws ? [
  {
    name: 'aws-access-key-id'
    value: awsAccessKeyId
  }
  {
    name: 'aws-secret-access-key'
    value: awsSecretAccessKey
  }
] : []

var awsEnv = hasAws ? [
  { name: 'AWS_REGION', value: awsRegion }
  { name: 'AWS_ACCESS_KEY_ID', secretRef: 'aws-access-key-id' }
  { name: 'AWS_SECRET_ACCESS_KEY', secretRef: 'aws-secret-access-key' }
] : []

// Absent rather than blank, like the others: Container Apps rejects a secret
// with an empty value.
var hasGoogle = !empty(googleClientId) && !empty(googleClientSecret)

var googleSecrets = hasGoogle ? [
  {
    name: 'google-client-secret'
    value: googleClientSecret
  }
] : []

var googleEnv = hasGoogle ? [
  { name: 'Google__ClientId', value: googleClientId }
  { name: 'Google__ClientSecret', secretRef: 'google-client-secret' }
  { name: 'Google__RedirectUri', value: googleRedirectUri }
] : []

// Plain rather than absent-when-empty, unlike the secrets above: this is not a
// secret, so it needs no Container Apps secret entry, and atlas has a working
// localhost default for it. An empty value is a misconfiguration worth seeing
// in the template rather than a deployment worth blocking.
var portalEnv = empty(publicBaseUrl) ? [] : [
  { name: 'PublicBaseUrl', value: publicBaseUrl }
]

// Where resumes go. No key and no connection string: the account name plus the
// identity is the whole configuration, which is the point of choosing an
// object store in the subscription we already own. There is nothing here that
// would be a secret if it leaked.
var resumeEnv = [
  { name: 'Resumes__AccountName', value: resumeStorageAccount }
  { name: 'Resumes__Container', value: 'resumes' }
  { name: 'Resumes__ClientId', value: pullIdentityClientId }
]

var sentryEnv = hasSentry ? [
  { name: 'Sentry__Dsn', secretRef: 'sentry-dsn' }
  // The deployed commit, so a spike in errors ties to what shipped rather
  // than being matched up by timestamp.
  { name: 'Sentry__Release', value: imageTag }
] : []

// ------------------------------------------------------------------ atlas ---
// Internal ingress. Harbor is the only path to the API. Atlas validates its
// own sessions and permissions rather than trusting the gateway, but there is
// still no reason to also publish it.
resource atlas 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-atlas-${environmentName}'
  location: location
  identity: pullIdentityConfig
  tags: union(tags, { service: 'atlas' })
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      ingress: {
        external: false
        targetPort: 8080
        transport: 'auto'
      }
      registries: registryConfig
      secrets: concat([dbSecret], sentrySecrets, googleSecrets)
    }
    template: {
      containers: [
        {
          name: 'atlas'
          image: '${registry.properties.loginServer}/atlas:${imageTag}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat([
            { name: 'ARCTIC_DB', secretRef: 'db-connection' }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            // Two proxies sit in front of atlas, not one: harbor, and then the
            // Container Apps internal ingress that fronts every app in the
            // environment. With one, atlas stops at the internal ingress and
            // records that instead of the caller — which is what made
            // sessions.ip read 100.100.x and put every request in a single
            // rate-limit bucket.
            { name: 'Network__ForwardLimit', value: '2' }
            // Liveness only, and deliberately not the database: a Postgres
            // blip that restarts every replica turns a recoverable problem
            // into an outage.
            { name: 'ASPNETCORE_ENVIRONMENT', value: environmentName == 'prod' ? 'Production' : 'Staging' }
          ], sentryEnv, googleEnv, portalEnv, resumeEnv)
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health', port: 8080 }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
}

// ------------------------------------------------------------------- lark ---
// No ingress at all, and never zero replicas. Nothing would wake it from zero,
// and a queue with no worker is a queue that silently stops sending while
// every dashboard reads green.
resource lark 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-lark-${environmentName}'
  location: location
  identity: pullIdentityConfig
  tags: union(tags, { service: 'lark' })
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      registries: registryConfig
      secrets: concat([dbSecret], sentrySecrets, awsSecrets)
    }
    template: {
      containers: [
        {
          name: 'lark'
          image: '${registry.properties.loginServer}/lark:${imageTag}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat([
            { name: 'ARCTIC_DB', secretRef: 'db-connection' }
            { name: 'DOTNET_ENVIRONMENT', value: environmentName == 'prod' ? 'Production' : 'Staging' }
          ], sentryEnv, awsEnv)
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

// ----------------------------------------------------------------- harbor ---
// The only thing exposed to the internet.
resource harbor 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-harbor-${environmentName}'
  location: location
  identity: pullIdentityConfig
  tags: union(tags, { service: 'harbor' })
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
      registries: registryConfig
      secrets: sentrySecrets
    }
    template: {
      containers: [
        {
          name: 'harbor'
          image: '${registry.properties.loginServer}/harbor:${imageTag}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat([
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'ReverseProxy__Clusters__atlas__Destinations__primary__Address', value: 'https://${atlas.properties.configuration.ingress.fqdn}/' }
            { name: 'ASPNETCORE_ENVIRONMENT', value: environmentName == 'prod' ? 'Production' : 'Staging' }
            // Exactly one proxy sits in front of harbor: the Container Apps
            // ingress, which appends the real client to X-Forwarded-For. One
            // means we take that entry and stop.
            //
            // Two was a hole rather than a margin. A caller who sends their
            // own X-Forwarded-For has it appended to, not replaced, so
            // consuming a second entry reaches the value they chose — proven
            // against staging, where a forged header became the client IP and
            // made every per-IP rate limit trivially bypassable.
            { name: 'Network__ForwardLimit', value: '1' }
          ], sentryEnv)
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/api/health', port: 8080 }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
}

output harborFqdn string = harbor.properties.configuration.ingress.fqdn
