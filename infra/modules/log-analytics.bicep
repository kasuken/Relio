param location string
param tags object
param workspaceName string

resource workspace 'Microsoft.OperationalInsights/workspaces@2026-03-01' = {
  name: workspaceName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
  }
}

output workspaceId string = workspace.id
output workspaceCustomerId string = workspace.properties.customerId
