// Holds the sha-<commit> images CI builds (Sprint 2) and pushes (Sprint 5).
param name string
param location string
param tags object

@description('Principals that pull images: one per workload identity.')
param pullPrincipalIds string[]
var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    // Pulls use the managed identity and pushes use CI's OIDC identity: no shared admin password.
    adminUserEnabled: false
  }
}

// The name depends only on the principal, not its position in the list, so adding a principal
// leaves the existing assignments untouched.
resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principalId in pullPrincipalIds: {
    name: guid(registry.id, principalId, acrPullRoleId)
    scope: registry
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
      principalId: principalId
      principalType: 'ServicePrincipal'
    }
  }
]

output name string = registry.name
output loginServer string = registry.properties.loginServer
