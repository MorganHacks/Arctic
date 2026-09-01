// The container registry, which outlives any one environment.
//
// Deployed to its own resource group so that deleting an environment cannot
// take the images with it — including the image a rollback needs.

targetScope = 'resourceGroup'

@description('Globally unique. Lowercase alphanumeric only.')
param registryName string

param location string = resourceGroup().location

param tags object = {}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: registryName
  location: location
  tags: tags
  sku: {
    // Basic holds a handful of small images. This is not a public feed.
    name: 'Basic'
  }
  properties: {
    // Off. Services pull with a managed identity granted AcrPull, so there is
    // no shared static password to leak, and nothing to rotate across every
    // service at once.
    adminUserEnabled: false
  }
}

output loginServer string = registry.properties.loginServer
