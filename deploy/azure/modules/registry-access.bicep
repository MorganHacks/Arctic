// Lets one environment's identity pull from the shared registry.
//
// Deployed into the registry's own resource group, because a role assignment
// lives at the scope it grants over. AcrPull and nothing else: pulling images
// is the only thing a running service needs, and push belongs to CI.

targetScope = 'resourceGroup'

param registryName string

@description('Principal id of the environment identity being granted access.')
param principalId string

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

// The built-in AcrPull role. Referenced by its id rather than by name, because
// the name is display text and the id is the contract.
var acrPull = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '7f951dda-4ed3-4680-a7ca-43fe172d538d')

resource pullAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  // Deterministic, so re-running produces the same assignment rather than a
  // second one.
  name: guid(registry.id, principalId, acrPull)
  properties: {
    roleDefinitionId: acrPull
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
