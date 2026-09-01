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
param registryResourceGroup string = 'rg-morganhacks-shared'

@secure()
param dbPassword string

param dbAdminUser string = 'arctic'
param dbName string = 'morganhacks'

@secure()
@description('Empty disables error reporting, which is what lets this run with no accounts.')
param sentryDsn string = ''

param tags object = {}

var suffix = 'morganhacks-${environmentName}'

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
    username: registry.listCredentials().username
    passwordSecretRef: 'registry-password'
  }
]

var registrySecret = {
  name: 'registry-password'
  value: registry.listCredentials().passwords[0].value
}

var dbSecret = {
  name: 'db-connection'
  value: connectionString
}

var sentrySecret = {
  name: 'sentry-dsn'
  value: sentryDsn
}

// ------------------------------------------------------------------ atlas ---
// Internal ingress. Harbor is the only path to the API. Atlas validates its
// own sessions and permissions rather than trusting the gateway, but there is
// still no reason to also publish it.
resource atlas 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-atlas-${environmentName}'
  location: location
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
      secrets: [registrySecret, dbSecret, sentrySecret]
    }
    template: {
      containers: [
        {
          name: 'atlas'
          image: '${registry.properties.loginServer}/atlas:${imageTag}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'ARCTIC_DB', secretRef: 'db-connection' }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'Sentry__Dsn', secretRef: 'sentry-dsn' }
            { name: 'Sentry__Release', value: imageTag }
            // Liveness only, and deliberately not the database: a Postgres
            // blip that restarts every replica turns a recoverable problem
            // into an outage.
            { name: 'ASPNETCORE_ENVIRONMENT', value: environmentName == 'prod' ? 'Production' : 'Staging' }
          ]
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
  tags: union(tags, { service: 'lark' })
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      registries: registryConfig
      secrets: [registrySecret, dbSecret, sentrySecret]
    }
    template: {
      containers: [
        {
          name: 'lark'
          image: '${registry.properties.loginServer}/lark:${imageTag}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'ARCTIC_DB', secretRef: 'db-connection' }
            { name: 'Sentry__Dsn', secretRef: 'sentry-dsn' }
            { name: 'Sentry__Release', value: imageTag }
            { name: 'DOTNET_ENVIRONMENT', value: environmentName == 'prod' ? 'Production' : 'Staging' }
          ]
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
      secrets: [registrySecret, sentrySecret]
    }
    template: {
      containers: [
        {
          name: 'harbor'
          image: '${registry.properties.loginServer}/harbor:${imageTag}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'ReverseProxy__Clusters__atlas__Destinations__primary__Address', value: 'https://${atlas.properties.configuration.ingress.fqdn}/' }
            { name: 'Sentry__Dsn', secretRef: 'sentry-dsn' }
            { name: 'Sentry__Release', value: imageTag }
            { name: 'ASPNETCORE_ENVIRONMENT', value: environmentName == 'prod' ? 'Production' : 'Staging' }
            // Container Apps terminates in front of harbor, so without this
            // RemoteIpAddress is the platform's and every per-IP rate limit
            // shares one bucket for the entire internet.
            { name: 'Network__ForwardLimit', value: '2' }
          ]
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
