// The shared Data Protection key ring (DataProtection__BlobUri), so cookies survive restarts and
// work on every replica (PHASE_3_PLAN.md §2, first row).
param name string
param location string
param tags object

@description('The shared app identity.')
param appPrincipalId string

var blobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    // Entra ID only: no account keys or SAS tokens exist to leak. The app uses its identity.
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
}

resource dpkeys 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'dpkeys'
  properties: {
    publicAccess: 'None'
  }
}

// Scoped to the one container, not the account.
resource appBlobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dpkeys.id, appPrincipalId, blobDataContributorRoleId)
  scope: dpkeys
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributorRoleId)
    principalId: appPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output dataProtectionBlobUri string = '${account.properties.primaryEndpoints.blob}${dpkeys.name}/keys.xml'
