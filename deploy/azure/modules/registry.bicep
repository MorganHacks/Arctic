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
    // Enabled because Container Apps pulls with a username and password.
    // A managed identity is the better answer and is a change here plus the
    // registries block in main.bicep.
    adminUserEnabled: true
  }
}

output loginServer string = registry.properties.loginServer
