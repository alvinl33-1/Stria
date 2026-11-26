// A user-assigned identity. main.bicep creates three: one for stria-server and the migration job,
// one for the sample service (5.4a), and one the GitHub deploy job signs in as (Sprint 5). Each
// exists before anything uses it, so its role assignments are already in place on the first pull
// and the first token.
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

@description('Also the PostgreSQL role name (Sprint 4 creates it with pgaadauth_create_principal).')
output name string = identity.name
