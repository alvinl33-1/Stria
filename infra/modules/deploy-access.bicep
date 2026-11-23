// How the GitHub deploy job (Sprint 5) signs in, and what it may change. There is no secret: Entra
// trusts GitHub's OIDC token for one exact subject (this repository, environment `demo`) and
// exchanges it for a token of the deploy identity. Each role is the narrowest built-in role on the
// one resource the job touches: push images, update the two apps and the job, read the database.
@description('Name of the deploy identity (identity.bicep) that receives the federated credential.')
param identityName string

@description('Principal ID of the deploy identity.')
param principalId string

@description('Exact OIDC subject of the deploy job. This repository uses immutable subjects (owner and repository IDs), not the owner/name form.')
param githubSubject string

param registryName string
param serverAppName string
param sampleAppName string
param migrateJobName string
param appIdentityName string
param postgresServerName string

var acrPushRoleId = '8311e382-0749-4cb8-b61a-304f252e45ec'
var containerAppsContributorRoleId = '358470bc-b998-42bd-ab17-a7e34c199c0f'
var containerAppsJobsContributorRoleId = '4e3d2b60-56ae-4dc6-a233-09c8e5a82e68'
// Updating an app or job that runs as a user-assigned identity needs assign/action on that identity.
var managedIdentityOperatorRoleId = 'f1a07417-d97a-45cb-824c-7a7467783830'
var readerRoleId = 'acdd72a7-3385-48ef-bd42-f606fba81ae7'

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: identityName
}

resource github 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: identity
  name: 'github-demo'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: githubSubject
    audiences: [
      'api://AzureADTokenExchange'
    ]
  }
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

resource server 'Microsoft.App/containerApps@2024-03-01' existing = {
  name: serverAppName
}

resource sample 'Microsoft.App/containerApps@2024-03-01' existing = {
  name: sampleAppName
}

resource job 'Microsoft.App/jobs@2024-03-01' existing = {
  name: migrateJobName
}

resource appIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: appIdentityName
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' existing = {
  name: postgresServerName
}

resource registryPush 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, principalId, acrPushRoleId)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPushRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

resource serverContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(server.id, principalId, containerAppsContributorRoleId)
  scope: server
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', containerAppsContributorRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

resource sampleContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(sample.id, principalId, containerAppsContributorRoleId)
  scope: sample
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', containerAppsContributorRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

resource jobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(job.id, principalId, containerAppsJobsContributorRoleId)
  scope: job
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      containerAppsJobsContributorRoleId
    )
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

resource appIdentityOperator 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(appIdentity.id, principalId, managedIdentityOperatorRoleId)
  scope: appIdentity
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', managedIdentityOperatorRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

resource postgresReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(postgres.id, principalId, readerRoleId)
  scope: postgres
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', readerRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
