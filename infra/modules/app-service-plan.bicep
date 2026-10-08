param location string
param tags object
param planName string

resource appServicePlan 'Microsoft.Web/serverfarms@2026-08-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: 'B1'
    tier: 'Basic'
    size: 'B1'
    family: 'B'
    capacity: 1
  }
  properties: {
    reserved: true
  }
}

output planId string = appServicePlan.id
