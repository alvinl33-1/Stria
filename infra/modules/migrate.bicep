// stria-migrate: runs the EF bundle (the `migrator` image) once per release, before the new revision.
param name string
param location string
param tags object
param environmentId string
param identityId string
param identityClientId string
param registryServer string
param image string
param connectionString string

resource job 'Microsoft.App/jobs@2024-03-01' = {
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
      triggerType: 'Manual'
      replicaTimeout: 600
      // A failed migration needs a person, not a retry: the deploy stops at the first failure.
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      registries: [
        {
          server: registryServer
          identity: identityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: image
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          // StriaDbContextDesignTimeFactory reads the same settings as the app.
          env: [
            { name: 'ConnectionStrings__Stria', value: connectionString }
            { name: 'Database__UseEntraAuth', value: 'true' }
            { name: 'AZURE_CLIENT_ID', value: identityClientId }
          ]
        }
      ]
    }
  }
}

output name string = job.name
