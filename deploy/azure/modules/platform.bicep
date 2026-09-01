// The platform: Postgres, the Container Apps environment, logging, and the
// migration job.
//
// Separate from apps.bicep so that migrations genuinely run before the code
// that depends on them. One template would update the job and the services in
// the same deployment, which puts new code in front of an old schema for
// however long the job takes — and that is the window every migration bug
// lives in.
//
// Declarative on purpose. A script has to be told how to reach the desired
// state and gets idempotency only where somebody remembered to hand-roll it;
// this describes the state itself, so re-deploying converges — including
// correcting anything changed by hand in the portal.
//
// The property that matters most is `what-if`: an infrastructure change can be
// reviewed before it happens, the same way a code change is.

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

@description('Postgres major version. Keep this the same as docker-compose and the tests.')
param postgresVersion string = '17'

param superAdminEmail string

param tags object = {}

@secure()
@description('Empty disables error reporting, which is what lets this run with no accounts.')
param sentryDsn string = ''

var suffix = 'morganhacks-${environmentName}'

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
  scope: resourceGroup(registryResourceGroup)
}

// ---------------------------------------------------------------- logging ---
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${suffix}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// --------------------------------------------------------------- postgres ---
resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: 'psql-${suffix}'
  location: location
  tags: tags
  sku: {
    // Smallest managed tier. Managed rather than self-hosted because the
    // database is the one place a mistake is permanent.
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: postgresVersion
    administratorLogin: dbAdminUser
    administratorLoginPassword: dbPassword
    storage: { storageSizeGB: 32 }
    backup: {
      // Said out loud rather than inherited from a default nobody checked.
      // An untested backup is a belief, not a backup.
      backupRetentionDays: 14
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: { mode: 'Disabled' }
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: dbName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

// Container Apps egress has no fixed address on the consumption plan, so this
// is the rule that lets the services connect at all. It permits other Azure
// services, not the open internet — but it is the weakest thing here, and the
// upgrade is VNet integration with a private endpoint.
resource allowAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: postgres
  name: 'allow-azure-services'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

var connectionString = 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=${dbName};Username=${dbAdminUser};Password=${dbPassword};SSL Mode=Require;Trust Server Certificate=true'

// -------------------------------------------------------- apps environment ---
resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${suffix}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

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

// ------------------------------------------------------------- migrations ---
// A job, not something the API does at startup. An API that migrates on boot
// means every replica racing to alter one schema, which is the documented way
// setups like this break.
resource migrations 'Microsoft.App/jobs@2024-03-01' = {
  name: 'caj-migrations-${environmentName}'
  location: location
  tags: union(tags, { service: 'migrations' })
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 600
      replicaRetryLimit: 1
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      registries: registryConfig
      secrets: [registrySecret, dbSecret]
    }
    template: {
      containers: [
        {
          name: 'migrations'
          image: '${registry.properties.loginServer}/migrations:${imageTag}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'ARCTIC_DB', secretRef: 'db-connection' }
            { name: 'ARCTIC_SUPER_ADMIN_EMAIL', value: superAdminEmail }
          ]
        }
      ]
    }
  }
  dependsOn: [database, allowAzure]
}


output environmentId string = environment.id
output postgresHost string = postgres.properties.fullyQualifiedDomainName
output registryLoginServer string = registry.properties.loginServer
