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
param registryResourceGroup string = 'rg-mh-shared'

@secure()
param dbPassword string

param dbAdminUser string = 'arctic'
param dbName string = 'morganhacks'

@description('Postgres major version. Keep this the same as docker-compose and the tests.')
param postgresVersion string = '18'

param superAdminEmail string

@description('Resource id of the identity that pulls images.')
param pullIdentityId string

param tags object = {}

var suffix = 'mh-${environmentName}'

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

// Azure refuses to create an extension unless it is named here first, whatever
// the connecting user's privileges are. citext is what makes email columns
// case-insensitive at the database rather than by remembering lower() at every
// call site, so without this the notify schema cannot be created at all.
resource extensions 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: postgres
  name: 'azure.extensions'
  properties: {
    value: 'CITEXT'
    source: 'user-override'
  }
}

// 0.0.0.0-0.0.0.0 is Azure's "any Azure service" rule, and it is the weakest
// thing in this file: any resource in any tenant can open a connection, with
// only the password in the way.
//
// It is here because a consumption environment has no fixed egress address.
// The environment's staticIp is its *ingress* — narrowing the rule to it was
// tried against staging and every connection failed, which is worth recording
// so nobody tries it a second time.
//
// The real fix is VNet integration with a private endpoint. VNet cannot be
// added to an existing Container Apps environment, so it means building a new
// one — worth doing before production holds real applicant data, and not worth
// rebuilding staging for on its own.
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

// ------------------------------------------------------------- migrations ---
// A job, not something the API does at startup. An API that migrates on boot
// means every replica racing to alter one schema, which is the documented way
// setups like this break.
resource migrations 'Microsoft.App/jobs@2024-03-01' = {
  name: 'caj-migrations-${environmentName}'
  location: location
  identity: pullIdentityConfig
  tags: union(tags, { service: 'migrations' })
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 600
      replicaRetryLimit: 1
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      registries: registryConfig
      secrets: [dbSecret]
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
  dependsOn: [database, allowAzure, extensions]
}


output environmentId string = environment.id
output postgresHost string = postgres.properties.fullyQualifiedDomainName
output registryLoginServer string = registry.properties.loginServer
