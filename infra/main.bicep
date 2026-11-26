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

@description('Entra tenant that signs users in (the test tenant). Not secret.')
param entraTenantId string = ''

@description('Client ID of the Stria app registration. Not secret.')
param entraClientId string = ''

@description('OIDC subject of the GitHub deploy job (Sprint 5): immutable form, owner and repository IDs, then the environment. Not secret.')
param githubDeploySubject string = 'repo:alvinl33-1@174841578/Stria@1457068099:environment:demo'

@description('Image tag for all three images: sha-<commit> from CI (Sprint 2). Never latest.')
param imageTag string = ''

@description('False on the first provision: the apps need images in ACR and secrets in Key Vault first (Sprint 4).')
param deployApps bool = false

@description('Monthly cost budget in the billing currency. The amount is decided at the Sprint 4 gate (PHASE_3_PLAN.md §5).')
param budgetAmount int = 20

@description('First day of the first budget month, yyyy-MM-01. Empty skips the budget, as in CI and offline builds.')
param budgetStartDate string = ''

@description('Optional extra address for budget alerts. Resource group Owners are always notified.')
param budgetContactEmail string = ''

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

// 5.4a: the sample service gets its own identity, so it no longer holds the table owner's database
// role or the vault-wide secret access that stria-server needs.
module sampleIdentity 'modules/identity.bicep' = {
  name: 'sample-identity'
  params: {
    name: 'id-stria-sample-${environmentName}'
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
    pullPrincipalIds: [
      identity.outputs.principalId
      sampleIdentity.outputs.principalId
    ]
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
    samplePrincipalId: deployApps ? sampleIdentity.outputs.principalId : ''
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

module postgres 'modules/postgres.bicep' = {
  name: 'postgres'
  params: {
    name: 'psql-stria-${suffix}'
    location: location
    tags: tags
  }
}

module environment 'modules/environment.bicep' = {
  name: 'environment'
  params: {
    name: 'cae-stria-${environmentName}'
    location: location
    tags: tags
    workspaceName: monitoring.outputs.workspaceName
  }
}

// No password: Username is the identity's PostgreSQL role, and the token is the password.
// VerifyFull checks the server certificate and host name, because that token is a bearer credential.
var connectionString = 'Host=${postgres.outputs.fqdn};Port=5432;Database=${postgres.outputs.databaseName};Username=${identity.outputs.name};Ssl Mode=VerifyFull'

module server 'modules/server.bicep' = if (deployApps) {
  name: 'server'
  params: {
    name: 'stria-server'
    location: location
    tags: tags
    environmentId: environment.outputs.id
    identityId: identity.outputs.id
    identityClientId: identity.outputs.clientId
    registryServer: registry.outputs.loginServer
    image: '${registry.outputs.loginServer}/stria-api:${imageTag}'
    keyVaultUri: keyVault.outputs.uri
    connectionString: connectionString
    dataProtectionBlobUri: storage.outputs.dataProtectionBlobUri
    dataProtectionKeyVaultKeyUri: keyVault.outputs.dataProtectionKeyUri
    entraTenantId: entraTenantId
    entraClientId: entraClientId
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
  }
}

module sample 'modules/sample.bicep' = if (deployApps) {
  name: 'sample'
  params: {
    name: 'sample-payment-gateway'
    location: location
    tags: tags
    environmentId: environment.outputs.id
    identityId: sampleIdentity.outputs.id
    registryServer: registry.outputs.loginServer
    image: '${registry.outputs.loginServer}/stria-sample:${imageTag}'
    keyVaultUri: keyVault.outputs.uri
    striaBaseUrl: 'https://${server!.outputs.fqdn}'
  }
}

module migrate 'modules/migrate.bicep' = if (deployApps) {
  name: 'migrate'
  params: {
    name: 'stria-migrate'
    location: location
    tags: tags
    environmentId: environment.outputs.id
    identityId: identity.outputs.id
    identityClientId: identity.outputs.clientId
    registryServer: registry.outputs.loginServer
    image: '${registry.outputs.loginServer}/stria-migrator:${imageTag}'
    connectionString: connectionString
  }
}

module budget 'modules/budget.bicep' = if (!empty(budgetStartDate)) {
  name: 'budget'
  params: {
    name: 'budget-stria-${environmentName}'
    amount: budgetAmount
    startDate: budgetStartDate
    contactEmail: budgetContactEmail
  }
}

// Sprint 5: the identity GitHub Actions deploys as, and its narrow rights on the apps above.
module deployIdentity 'modules/identity.bicep' = if (deployApps) {
  name: 'deploy-identity'
  params: {
    name: 'id-stria-deploy-${environmentName}'
    location: location
    tags: tags
  }
}

module deployAccess 'modules/deploy-access.bicep' = if (deployApps) {
  name: 'deploy-access'
  params: {
    identityName: deployIdentity!.outputs.name
    principalId: deployIdentity!.outputs.principalId
    githubSubject: githubDeploySubject
    registryName: registry.outputs.name
    serverAppName: server!.outputs.name
    sampleAppName: sample!.outputs.name
    migrateJobName: migrate!.outputs.name
    appIdentityName: identity.outputs.name
    sampleIdentityName: sampleIdentity.outputs.name
    postgresServerName: postgres.outputs.name
  }
}

// Upper-case outputs become azd environment values (Sprint 4 setup and Sprint 5 deploy read them).
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = registry.outputs.loginServer
output AZURE_KEY_VAULT_NAME string = keyVault.outputs.name
output AZURE_MANAGED_IDENTITY_CLIENT_ID string = identity.outputs.clientId
output POSTGRES_SERVER_NAME string = postgres.outputs.name
output POSTGRES_HOST string = postgres.outputs.fqdn
output STRIA_SERVER_URL string = deployApps ? 'https://${server!.outputs.fqdn}' : ''
output STRIA_MIGRATE_JOB_NAME string = deployApps ? migrate!.outputs.name : ''
output STRIA_DEPLOY_CLIENT_ID string = deployApps ? deployIdentity!.outputs.clientId : ''
// Consumed by the container app modules in ticket 3.2. They map one-to-one onto the Sprint 1
// settings (PHASE_3_PLAN.md, Step 1 table).
output identityId string = identity.outputs.id
output logAnalyticsWorkspaceName string = monitoring.outputs.workspaceName
output appInsightsConnectionString string = monitoring.outputs.appInsightsConnectionString
output dataProtectionBlobUri string = storage.outputs.dataProtectionBlobUri
output dataProtectionKeyVaultKeyUri string = keyVault.outputs.dataProtectionKeyUri
