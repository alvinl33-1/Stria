// Stria hosted demo: one resource group, created and torn down as a unit (PHASE_3_PLAN.md §3).
// Nothing here is provisioned until Sprint 4 is authorized in writing.
targetScope = 'resourceGroup'
@description('Short environment name used in resource names and the azd-env-name tag.')
@minLength(2)
@maxLength(12)

param environmentName string = 'demo'
@description('Region for every resource.')

param location string = resourceGroup().location
@description('Object ID of the operator who stores secrets during one-time setup (Sprint 4). Empty skips the assignment.')
param operatorPrincipalId string = ''
var tags = {
  'azd-env-name': environmentName
  project: 'stria'
}
// Registry, vault and storage names are global DNS names. uniqueString is deterministic, so the
// same resource group always gets the same names and redeploying never creates duplicates.
var suffix = uniqueString(resourceGroup().id)

module identity 'modules/identity.bicep' = {
  name: 'identity'
  params: {
    name: 'id-stria-${environmentName}'
    location: location
    tags: tags
  }
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
    params: {
    workspaceName: 'log-stria-${environmentName}'
    appInsightsName: 'appi-stria-${environmentName}'
    location: location
    tags: tags
  }
}

module registry 'modules/registry.bicep' = {
  name: 'registry'
  params: {
    name: 'crstria${suffix}'
    location: location
    tags: tags
    pullPrincipalId: identity.outputs.principalId
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    name: 'kv-stria-${suffix}'
    location: location
    tags: tags
    appPrincipalId: identity.outputs.principalId
    operatorPrincipalId: operatorPrincipalId
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    name: 'ststria${suffix}'
    location: location
    tags: tags
    appPrincipalId: identity.outputs.principalId
  }
}

// Upper-case outputs become azd environment values (Sprint 4 setup and Sprint 5 deploy read them).
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = registry.outputs.loginServer
output AZURE_KEY_VAULT_NAME string = keyVault.outputs.name
output AZURE_MANAGED_IDENTITY_CLIENT_ID string = identity.outputs.clientId
// Consumed by the container app modules in ticket 3.2. They map one-to-one onto the Sprint 1
// settings (PHASE_3_PLAN.md, Step 1 table).
output identityId string = identity.outputs.id
output logAnalyticsWorkspaceName string = monitoring.outputs.workspaceName
output appInsightsConnectionString string = monitoring.outputs.appInsightsConnectionString
output dataProtectionBlobUri string = storage.outputs.dataProtectionBlobUri
output dataProtectionKeyVaultKeyUri string = keyVault.outputs.dataProtectionKeyUri
