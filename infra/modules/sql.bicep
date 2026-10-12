param location string
param tags object
param sqlServerName string
param sqlDatabaseName string
param sqlAdminLogin string

@secure()
param sqlAdminPassword string

@description('Point-in-time restore window in days. The Basic tier allows 1 to 7, and 7 is the maximum.')
@minValue(1)
@maxValue(7)
param backupRetentionDays int = 7

@description('How long weekly long-term retention copies are kept, as an ISO 8601 duration (P4W = 4 weeks, PT0S = none).')
param longTermWeeklyRetention string = 'P4W'

@description('How long monthly long-term retention copies are kept (PT0S = none).')
param longTermMonthlyRetention string = 'PT0S'

@description('How long yearly long-term retention copies are kept (PT0S = none).')
param longTermYearlyRetention string = 'PT0S'

resource sqlServer 'Microsoft.Sql/servers@2025-01-01' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource allowAzureServicesFirewallRule 'Microsoft.Sql/servers/firewallRules@2025-01-01' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2025-01-01' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    maxSizeBytes: 2147483648
  }
}

// Backups (REL-005, docs/operations/backup-restore.md). Azure SQL takes full, differential and
// transaction log backups automatically and encrypts them with the database's transparent data
// encryption. These resources only make the retention explicit, so it is reviewed and monitored
// instead of an unstated default.
//
// A deleted account stays in these backups until they expire: about 7 days of point-in-time
// restore plus the long-term weekly copies (4 weeks), so up to 35 days. Keep that number in step
// with docs/operations/backup-restore.md and docs/security/account-erasure.md.
resource shortTermRetention 'Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies@2025-01-01' = {
  parent: sqlDatabase
  name: 'default'
  properties: {
    retentionDays: backupRetentionDays
  }
}

resource longTermRetention 'Microsoft.Sql/servers/databases/backupLongTermRetentionPolicies@2025-01-01' = {
  parent: sqlDatabase
  name: 'default'
  properties: {
    weeklyRetention: longTermWeeklyRetention
    monthlyRetention: longTermMonthlyRetention
    yearlyRetention: longTermYearlyRetention
    weekOfYear: 1
  }
}

// Service-managed transparent data encryption, enabled by default on new databases; declared so a
// drift to "disabled" is corrected on the next deployment.
resource transparentDataEncryption 'Microsoft.Sql/servers/databases/transparentDataEncryption@2025-01-01' = {
  parent: sqlDatabase
  name: 'current'
  properties: {
    state: 'Enabled'
  }
}

output serverName string = sqlServer.name
output databaseName string = sqlDatabase.name
output serverId string = sqlServer.id
output databaseId string = sqlDatabase.id
output backupRetentionDays int = shortTermRetention.properties.retentionDays
output longTermWeeklyRetention string = longTermRetention.properties.weeklyRetention
