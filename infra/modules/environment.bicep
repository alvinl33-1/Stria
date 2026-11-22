// The Container Apps environment: the shared network boundary and log sink for every app and job.
param name string
param location string
param tags object

@description('Log Analytics workspace from monitoring.bicep. Looked up by name so its key never leaves this module.')
param workspaceName string

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: workspaceName
}

// environmentMode must be explicit: without it Azure now creates an Express environment, which
// rejects jobs and Key Vault secret references (both used here). 2026-07-01 is the first GA API with it.
resource environment 'Microsoft.App/managedEnvironments@2026-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    environmentMode: 'WorkloadProfiles'
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
}

output id string = environment.id
