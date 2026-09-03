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

@description('''
Principal id of that same identity. It is what a role assignment grants to, and
it is a different value from the resource id — passing the wrong one deploys
without complaint and denies every request at run time.
''')
param pullIdentityPrincipalId string

param tags object = {}

var suffix = 'mh-${environmentName}'

// Globally unique, lowercase alphanumeric, at most 24 characters. Derived from
// the subscription and the environment rather than typed, so two environments
// cannot collide and a redeploy always lands on the same account.
var storageName = 'stmh${environmentName}${uniqueString(subscription().subscriptionId, environmentName)}'

var resumeContainerName = 'resumes'

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

    // Ingestion is charged per gigabyte and is the one line here that can run
    // away on its own: a service that starts logging every request, or a retry
    // loop that logs its own failure, bills for it. The cap sits a little over
    // ordinary volume, so a normal month is free and a fault is bounded rather
    // than open-ended.
    //
    // Hitting it stops ingestion until the next day, which loses telemetry and
    // not data. That is the right way round — and if it is ever hit, the thing
    // to fix is whatever started shouting, not this number.
    workspaceCapping: { dailyQuotaGb: json('0.5') }
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

// ---------------------------------------------------------------- resumes ---
// Azure Blob rather than the Cloudflare R2 the build plan recommends.
//
// The plan's argument for R2 is egress cost and portability. Neither survives
// contact with what this is: a few hundred PDFs a year, read by a dozen
// reviewers, where the egress bill is rounding error either way. What we get
// instead is that it is declared here, in the same deployment as everything
// else, with no second account to open, no second bill, and no access key to
// store or rotate — atlas reaches it with the managed identity it already
// holds. The portability the plan actually cares about is that the database
// stores a key rather than a URL, and that is bought in the code by putting an
// interface in front of this, not by which vendor holds the bytes.
//
// Every property below that says "no" is the point of the resource. These are
// files uploaded by strangers and opened by organizers in a browser.
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  // Storage account names are globally unique and allow no hyphens, so this
  // cannot follow the `st-mh-<env>` shape the rest of the file uses. The
  // environment is still in the name, because running a command against the
  // wrong environment is the mistake worth making hard.
  name: storageName
  location: location
  tags: union(tags, { service: 'resumes' })
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    // The single most important line here. Without it a container can be
    // flipped to anonymous read from the portal by somebody who does not
    // realise what is in it, and every resume becomes a public URL.
    allowBlobPublicAccess: false

    // No account key, for anybody, ever. It closes off the credential that
    // cannot be scoped, cannot be attributed to a person, and does not expire:
    // with this set, the only way in is an identity Azure can revoke, and the
    // read links atlas issues are signed with a user delegation key rather
    // than with a secret sitting in configuration.
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true

    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'

    // Reachable from the internet, and it has to be: the signed link is opened
    // by a reviewer's own browser, so a private endpoint would mean proxying
    // every resume through the API. The access control is the SAS and its
    // five-minute life, not the network — an unsigned request to a blob here
    // is refused whether it came from inside the VNet or not.
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

resource blobs 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    // A deleted resume is recoverable for a month. Applications get withdrawn
    // and rows get corrected, and the version of that mistake nobody can undo
    // is the one where an applicant is asked to send their CV again.
    deleteRetentionPolicy: {
      enabled: true
      days: 30
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: 30
    }
  }
}

resource resumeContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobs
  name: resumeContainerName
  properties: {
    // Said out loud rather than left to the default. This is the property that
    // decides whether a stranger holding a URL can read somebody's CV.
    publicAccess: 'None'
  }
}

// The built-in Storage Blob Data Contributor role, referenced by id because
// the name is display text and the id is the contract. Contributor rather than
// Reader because atlas writes the uploads; the same role also carries
// generateUserDelegationKey, which is what lets it sign a read link without
// ever holding an account key.
//
// Scoped to the storage account and nothing wider. The identity can read and
// write blobs here and cannot see the subscription it is in.
var blobDataContributor = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'ba92f5b4-2d11-453d-a403-e96b0029c9fe')

resource resumeAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  // Deterministic, so re-running converges on one assignment rather than
  // adding another. Same reasoning as the registry's AcrPull grant.
  name: guid(storage.id, pullIdentityPrincipalId, blobDataContributor)
  properties: {
    roleDefinitionId: blobDataContributor
    principalId: pullIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

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
output resumeStorageAccount string = storage.name
output resumeContainer string = resumeContainer.name
