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

// centralus rather than eastus: eastus is capacity-restricted for this
// subscription, and Postgres provisioning is refused there outright. centralus
// also offers Postgres 18, which is what docker-compose and the tests run.
param location string = 'centralus'

@description('Commit sha. Never "latest" — a rollback has to be a tag that already exists.')
param imageTag string

@description('Globally unique, lowercase alphanumeric.')
param registryName string = 'crmharctic'

@secure()
param dbPassword string

param superAdminEmail string

@secure()
@description('Empty disables error reporting, which is what lets this run with no accounts.')
param sentryDsn string = ''

@description('SES region. Empty means lark runs but sends nothing.')
param awsRegion string = ''

@secure()
param awsAccessKeyId string = ''

@secure()
param awsSecretAccessKey string = ''

@description('Google OAuth client id. Empty leaves organizer sign-in answering 503.')
param googleClientId string = ''

@secure()
param googleClientSecret string = ''

@description('Where Google sends the browser back. The admin app origin, not harbor.')
param googleRedirectUri string = ''

@description('''
Where applicants are, as opposed to where the API is: the portalweb origin.
Emailed sign-in links are built from this, so an empty or wrong value sends
every hacker to a link that goes nowhere.
''')
param publicBaseUrl string = ''

@description('Postgres major version. Keep this the same as docker-compose and the tests.')
param postgresVersion string = '18'

@description('''
Replicas of the web-facing services kept warm rather than asleep.

Zero costs almost nothing and answers the first request after an idle spell a
few seconds late. One removes that delay and costs roughly thirty dollars a
month more. Set it to one while registration is open and for the event
weekend; leave it at zero the rest of the year.
''')
@minValue(0)
@maxValue(3)
param warmReplicas int = 0

@description('''
False on the first pass. The registry has to exist and hold the images before
the migration job can be created — Container Apps validates that the image is
really there — so pass one is the registry alone.
''')
param deployPlatform bool = true

@description('''
False until migrations have run. Otherwise the services are updated in the same
deployment as the job, which puts new code in front of an old schema for
however long that job takes.
''')
param deployApps bool = true

// The registry outlives any one environment, and lives in its own group so
// deleting an environment cannot take the images with it — including the image
// a rollback needs.
// Azure's own abbreviations rather than something invented here, so a group
// reads by type and anyone who has used Azure before needs no explanation.
// Conventions are in naming.md.
var sharedGroupName = 'rg-mh-shared'
var groupName = 'rg-mh-${environmentName}'

// On every resource. Cost Analysis groups by these, and next year's team
// inherits a subscription where "what is this and can I delete it" has an
// answer rather than being a guess.
var commonTags = {
  workload: 'mh'
  environment: environmentName
  managedBy: 'bicep'
  repository: 'MorganHacks/Arctic'
}

// The registry outlives any one environment, so it is not tagged as belonging
// to either.
var sharedTags = {
  workload: 'mh'
  environment: 'shared'
  managedBy: 'bicep'
  repository: 'MorganHacks/Arctic'
}

resource sharedGroup 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: sharedGroupName
  location: location
  tags: sharedTags
}

resource group 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: groupName
  location: location
  tags: commonTags
}

module registry 'modules/registry.bicep' = {
  name: 'registry'
  scope: sharedGroup
  params: {
    registryName: registryName
    location: location
    tags: sharedTags
  }
}

// The identity every service pulls with, and its AcrPull grant on the shared
// registry. Both have to exist before anything tries to start, because a
// container app that cannot pull its image never runs at all.
module pullIdentity 'modules/pull-identity.bicep' = {
  name: 'pull-identity'
  scope: group
  params: {
    environmentName: environmentName
    location: location
    tags: commonTags
  }
}

module registryAccess 'modules/registry-access.bicep' = {
  name: 'registry-access'
  scope: sharedGroup
  params: {
    registryName: registryName
    principalId: pullIdentity.outputs.principalId
  }
  dependsOn: [registry]
}

module platform 'modules/platform.bicep' = if (deployPlatform) {
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
    pullIdentityId: pullIdentity.outputs.id
    pullIdentityPrincipalId: pullIdentity.outputs.principalId
    tags: commonTags
  }
  dependsOn: [registry, registryAccess]
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
    awsRegion: awsRegion
    awsAccessKeyId: awsAccessKeyId
    awsSecretAccessKey: awsSecretAccessKey
    googleClientId: googleClientId
    googleClientSecret: googleClientSecret
    googleRedirectUri: googleRedirectUri
    publicBaseUrl: publicBaseUrl
    warmReplicas: warmReplicas
    pullIdentityId: pullIdentity.outputs.id
    pullIdentityClientId: pullIdentity.outputs.clientId
    tags: commonTags
  }
  dependsOn: [platform, registryAccess]
}

output registryLoginServer string = registry.outputs.loginServer
output postgresHost string = deployPlatform ? platform!.outputs.postgresHost : ''
output harborFqdn string = deployApps ? apps!.outputs.harborFqdn : ''
