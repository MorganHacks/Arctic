// The front door. Everything is reached from here.
//
// Subscription-scoped, so it owns the resource groups too — an environment is
// then one thing that either exists or does not, rather than a group somebody
// has to remember to create first.
//
//   az deployment sub create -l eastus -f main.bicep -p staging.bicepparam
//
// Deployed in two passes, and that is deliberate. `deployApps` is false on the
// first pass so the migration job can run against the new schema before any
// service that expects it is updated. One pass would put new code in front of
// an old schema for however long migrations take, and that window is where
// every migration bug lives. deploy.sh handles the sequence.

targetScope = 'subscription'

@allowed(['staging', 'prod'])
param environmentName string

param location string = 'eastus'

@description('Commit sha. Never "latest" — a rollback has to be a tag that already exists.')
param imageTag string

@description('Globally unique, lowercase alphanumeric.')
param registryName string = 'morganhacksacr'

@secure()
param dbPassword string

param superAdminEmail string

@secure()
@description('Empty disables error reporting, which is what lets this run with no accounts.')
param sentryDsn string = ''

@description('Postgres major version. Keep this the same as docker-compose and the tests.')
param postgresVersion string = '17'

@description('False on the first pass, so migrations run before the services are updated.')
param deployApps bool = true

// The registry outlives any one environment, and lives in its own group so
// deleting an environment cannot take the images with it — including the image
// a rollback needs.
var sharedGroupName = 'morganhacks-shared'
var groupName = 'morganhacks-${environmentName}'

resource sharedGroup 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: sharedGroupName
  location: location
}

resource group 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: groupName
  location: location
}

module registry 'modules/registry.bicep' = {
  name: 'registry'
  scope: sharedGroup
  params: {
    registryName: registryName
    location: location
  }
}

module platform 'modules/platform.bicep' = {
  name: 'platform'
  scope: group
  params: {
    environmentName: environmentName
    location: location
    imageTag: imageTag
    registryName: registryName
    registryResourceGroup: sharedGroupName
    dbPassword: dbPassword
    postgresVersion: postgresVersion
    superAdminEmail: superAdminEmail
  }
  dependsOn: [registry]
}

module apps 'modules/apps.bicep' = if (deployApps) {
  name: 'apps'
  scope: group
  params: {
    environmentName: environmentName
    location: location
    imageTag: imageTag
    registryName: registryName
    registryResourceGroup: sharedGroupName
    dbPassword: dbPassword
    sentryDsn: sentryDsn
  }
  dependsOn: [platform]
}

output postgresHost string = platform.outputs.postgresHost
output registryLoginServer string = platform.outputs.registryLoginServer
output harborFqdn string = deployApps ? apps!.outputs.harborFqdn : ''
