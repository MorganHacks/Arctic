// The identity the services use to pull their own images.
//
// Replaces the registry's admin password. That password is a static credential
// shared by every service, readable by anyone with access to the resource, and
// rotating it means redeploying everything at once. An identity is scoped to
// this environment, grants exactly AcrPull, and has nothing to leak.

targetScope = 'resourceGroup'

param environmentName string
param location string = resourceGroup().location
param tags object = {}

resource pullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-mh-${environmentName}-pull'
  location: location
  tags: tags
}

output id string = pullIdentity.id
output principalId string = pullIdentity.properties.principalId
