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

// Needed by anything that authenticates *as* this identity from inside a
// container, rather than merely being assigned it. A container app with more
// than one identity available has to be told which to use, and the Azure SDK
// asks for the client id — with nothing set it picks the system-assigned one,
// which holds no roles, and every call fails as if the role assignment were
// missing.
output clientId string = pullIdentity.properties.clientId
