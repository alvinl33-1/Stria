// Secrets (the Entra client secret and the data-plane key, stored in Sprint 4) and the key that
// wraps the Data Protection key ring (DataProtection__KeyVaultKeyUri).
param name string
param location string
param tags object

@description('The shared app identity.')
param appPrincipalId string

@description('Operator who stores secrets during one-time setup. Empty skips the assignment.')
param operatorPrincipalId string = ''

var secretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'
var secretsOfficerRoleId = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
var cryptoServiceEncryptionUserRoleId = 'e147488a-f6f5-4113-8e2d-b22465e65bf6'

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    // Azure RBAC role assignments, not vault access policies: one permission model everywhere.
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    // Purge protection is omitted (it can be turned on but never set to false). With it on, a
    // torn-down demo vault's name stays reserved for the whole retention period.
    publicNetworkAccess: 'Enabled'
  }
}

// Each wrapped key in keys.xml records the key version that wrapped it, so a new version of this
// key never invalidates existing cookies or antiforgery tokens.
resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: vault
  name: 'dataprotection'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: [
      'wrapKey'
      'unwrapKey'
    ]
  }
}

resource appSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, appPrincipalId, secretsUserRoleId)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', secretsUserRoleId)
    principalId: appPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Scoped to the one key, not the vault: the app can wrap and unwrap with nothing else.
resource appKeyWrapper 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataProtectionKey.id, appPrincipalId, cryptoServiceEncryptionUserRoleId)
  scope: dataProtectionKey
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cryptoServiceEncryptionUserRoleId)
    principalId: appPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Owner on the subscription can't write secrets to an RBAC vault; this data-plane role can.
resource operatorSecretsOfficer 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(operatorPrincipalId)) {
  name: guid(vault.id, operatorPrincipalId, secretsOfficerRoleId)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', secretsOfficerRoleId)
    principalId: operatorPrincipalId
  }
}

output name string = vault.name
output uri string = vault.properties.vaultUri
@description('Versionless: Data Protection always wraps with the latest version.')
output dataProtectionKeyUri string = dataProtectionKey.properties.keyUri
