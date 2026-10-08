param location string
param tags object
param keyVaultName string
param sqlServerName string
param sqlDatabaseName string
param sqlAdminLogin string

@secure()
param sqlAdminPassword string

@secure()
param dpCertPassword string

var sqlConnectionString = 'Server=tcp:${sqlServerName}.database.windows.net,1433;Initial Catalog=${sqlDatabaseName};Persist Security Info=False;User ID=${sqlAdminLogin};Password=${sqlAdminPassword};MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

resource keyVault 'Microsoft.KeyVault/vaults@2026-05-15' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

resource sqlAdminPasswordSecret 'Microsoft.KeyVault/vaults/secrets@2026-05-15' = {
  parent: keyVault
  name: 'sql-admin-password'
  properties: {
    value: sqlAdminPassword
  }
}

resource sqlConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2026-05-15' = {
  parent: keyVault
  name: 'sql-connection-string'
  properties: {
    value: sqlConnectionString
  }
}

resource dataProtectionCertificatePasswordSecret 'Microsoft.KeyVault/vaults/secrets@2026-05-15' = {
  parent: keyVault
  name: 'dp-cert-password'
  properties: {
    value: dpCertPassword
  }
}

output vaultId string = keyVault.id
output vaultName string = keyVault.name
