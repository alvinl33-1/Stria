// One user-assigned identity for the apps and the migration job. It exists before any revision
// starts, so its role assignments are already in place on the first pull and the first token.
param name string
param location string
param tags object

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: name
  location: location
  tags: tags
}

output id string = identity.id
output principalId string = identity.properties.principalId
@description('Set as AZURE_CLIENT_ID so DefaultAzureCredential picks this identity.')
output clientId string = identity.properties.clientId
