// sample-payment-gateway: internal ingress only, so nothing outside the environment can reach it.
param name string
param location string
param tags object
param environmentId string

@description('The sample\'s own identity (5.4a): AcrPull and the one secret below, no database role.')
param identityId string

param registryServer string
param image string
param keyVaultUri string

@description('Public HTTPS URL of stria-server; the poller calls its data-plane route.')
param striaBaseUrl string

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
        external: false
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
      secrets: [
        {
          name: 'paymentgateway-key'
          keyVaultUrl: '${keyVaultUri}secrets/paymentgateway-key'
          identity: identityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'sample-payment-gateway'
          image: image
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'Stria__BaseUrl', value: striaBaseUrl }
            { name: 'Stria__ServiceName', value: 'PaymentGateway' }
            { name: 'Stria__ServiceKey', secretRef: 'paymentgateway-key' }
            { name: 'Stria__PollIntervalMs', value: '1000' }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/healthz', port: port }
              periodSeconds: 30
              failureThreshold: 3
            }
          ]
        }
      ]
      // While at zero nothing polls, which also lets stria-server scale to zero between demos.
      scale: {
        minReplicas: 0
        maxReplicas: 1
      }
    }
  }
}

output name string = app.name
