// stria-server: the only public app. One replica at most, because rate limits live in memory.
param name string
param location string
param tags object
param environmentId string
param identityId string
param identityClientId string
param registryServer string
param image string
param keyVaultUri string

@description('ADO.NET connection string with no password; the token provider supplies one.')
param connectionString string
param dataProtectionBlobUri string
param dataProtectionKeyVaultKeyUri string
param entraTenantId string
param entraClientId string

@secure()
param appInsightsConnectionString string

var port = 8080

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: {
    environmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: port
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: registryServer
          identity: identityId
        }
      ]
      // Key Vault references: the platform fetches the value with the identity. Versionless URLs
      // pick up a rotated secret on the next refresh, with no redeploy.
      secrets: [
        {
          name: 'entra-client-secret'
          keyVaultUrl: '${keyVaultUri}secrets/entra-client-secret'
          identity: identityId
        }
        {
          name: 'paymentgateway-key-sha256'
          keyVaultUrl: '${keyVaultUri}secrets/paymentgateway-key-sha256'
          identity: identityId
        }
        {
          name: 'appinsights-connection-string'
          value: appInsightsConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'stria-server'
          image: image
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          // Same names as compose.yaml, plus the Sprint 1 Azure settings (PHASE_3_PLAN.md Step 1 table).
          env: [
            { name: 'ConnectionStrings__Stria', value: connectionString }
            { name: 'Database__UseEntraAuth', value: 'true' }
            { name: 'Database__MigrateOnStartup', value: 'false' }
            { name: 'Retention__MaxAgeDays', value: '90' }
            { name: 'Retention__MaxClearedExperiments', value: '10000' }
            { name: 'ForwardedHeaders__Enabled', value: 'true' }
            { name: 'DataProtection__BlobUri', value: dataProtectionBlobUri }
            { name: 'DataProtection__KeyVaultKeyUri', value: dataProtectionKeyVaultKeyUri }
            { name: 'AZURE_CLIENT_ID', value: identityClientId }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'appinsights-connection-string' }
            { name: 'AzureAd__TenantId', value: entraTenantId }
            { name: 'AzureAd__ClientId', value: entraClientId }
            { name: 'AzureAd__ClientSecret', secretRef: 'entra-client-secret' }
            { name: 'AzureAdApi__TenantId', value: entraTenantId }
            { name: 'AzureAdApi__ClientId', value: entraClientId }
            { name: 'DataPlane__ServiceKeyHashes__PaymentGateway', secretRef: 'paymentgateway-key-sha256' }
          ]
          probes: [
            // Startup covers a cold start from zero: up to 60 s before liveness takes over.
            {
              type: 'Startup'
              httpGet: { path: '/healthz', port: port }
              initialDelaySeconds: 3
              periodSeconds: 3
              failureThreshold: 20
            }
            {
              type: 'Liveness'
              httpGet: { path: '/healthz', port: port }
              periodSeconds: 30
              failureThreshold: 3
            }
            // /readyz checks the database, so traffic stops while PostgreSQL is stopped.
            {
              type: 'Readiness'
              httpGet: { path: '/readyz', port: port }
              periodSeconds: 10
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
      }
    }
  }
}

output fqdn string = app.properties.configuration.ingress.fqdn
output name string = app.name
