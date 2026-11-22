// Logs from the Container Apps environment, and traces from StriaTelemetry (Sprint 1).
param workspaceName string
param appInsightsName string
param location string
param tags object

@description('Daily ingestion cap in GB. Past it, logs are dropped until the next day; it bounds the bill.')
param dailyQuotaGb string = '0.05'

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: workspaceName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: json(dailyQuotaGb)
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    // StriaTelemetry exports with the connection string alone. Disabling local auth would require
    // an Entra credential on the exporter as well.
    DisableLocalAuth: false
  }
}

output workspaceName string = workspace.name
output appInsightsConnectionString string = appInsights.properties.ConnectionString
