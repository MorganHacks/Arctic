// A separate manual job; never part of startup, migrations, or production deploys.
targetScope = 'resourceGroup'

@secure()
param dbConnectionString string
param imageTag string
param registryName string = 'crmharctic'
@minValue(1)
@maxValue(1000)
param applicantCount int = 50
param apply bool = false

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: 'cae-mh-staging'
}
resource pullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: 'id-mh-staging-pull'
}
resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
  scope: resourceGroup('rg-mh-shared')
}
resource seed 'Microsoft.App/jobs@2024-03-01' = {
  name: 'caj-seed-staging'
  location: resourceGroup().location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${pullIdentity.id}': {} }
  }
  tags: { environment: 'staging', service: 'seed' }
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 600
      replicaRetryLimit: 0
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      registries: [{ server: registry.properties.loginServer, identity: pullIdentity.id }]
      secrets: [{ name: 'db-connection', value: dbConnectionString }]
    }
    template: {
      containers: [{
        name: 'seed'
        image: '${registry.properties.loginServer}/hacker-seed:${imageTag}'
        resources: { cpu: json('0.5'), memory: '1Gi' }
        args: concat(['--staging', '--count', string(applicantCount)], apply ? ['--apply'] : [])
        env: [
          { name: 'ARCTIC_DB', secretRef: 'db-connection' }
          { name: 'ARCTIC_TARGET', value: 'staging' }
          { name: 'DOTNET_ENVIRONMENT', value: 'Staging' }
        ]
      }]
    }
  }
}
