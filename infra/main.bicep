targetScope = 'subscription'

@minLength(1)
@maxLength(64)
param environmentName string

@minLength(1)
param location string

@minLength(1)
param sessionId string

@minLength(1)
param deployedBy string

@minLength(1)
param createdAt string

@minLength(1)
param deployerObjectId string

@secure()
param sqlAdminPassword string

@secure()
param dpCertPassword string

param sqlAdminLogin string = 'relioadmin'

@description('Hosted billing: None (no plans or limits) or Stripe. See docs/security/billing.md before switching.')
@allowed([
  'None'
  'Stripe'
])
param billingProvider string = 'None'

@description('Stripe price id of Relio Pro billed monthly. Required when billingProvider is Stripe.')
param stripeProMonthlyPriceId string = ''

@description('Stripe price id of Relio Pro billed yearly. Required when billingProvider is Stripe.')
param stripeProYearlyPriceId string = ''

var resourceGroupName = 'rg-relio-prod'
var appServicePlanName = 'asp-relio-prod-edff'
var webAppName = 'app-relio-prod-edff'
var sqlServerName = 'sql-relio-prod-edff'
var sqlDatabaseName = 'relio'
var keyVaultName = 'kv-relio-prod-edff'
var logAnalyticsWorkspaceName = 'log-relio-prod-edff'

var tags = {
  'app-onboard-skill': 'true'
  'app-onboard-session-id': sessionId
  'created-at': createdAt
  environment: environmentName
  'deployed-by': deployedBy
}

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module logAnalytics './modules/log-analytics.bicep' = {
  name: 'logAnalytics'
  scope: rg
  params: {
    location: location
    tags: tags
    workspaceName: logAnalyticsWorkspaceName
  }
}

module appServicePlan './modules/app-service-plan.bicep' = {
  name: 'appServicePlan'
  scope: rg
  params: {
    location: location
    tags: tags
    planName: appServicePlanName
  }
}

module sql './modules/sql.bicep' = {
  name: 'sql'
  scope: rg
  params: {
    location: location
    tags: tags
    sqlServerName: sqlServerName
    sqlDatabaseName: sqlDatabaseName
    sqlAdminLogin: sqlAdminLogin
    sqlAdminPassword: sqlAdminPassword
  }
}

module keyVault './modules/key-vault.bicep' = {
  name: 'keyVault'
  scope: rg
  params: {
    location: location
    tags: tags
    keyVaultName: keyVaultName
    sqlServerName: sql.outputs.serverName
    sqlDatabaseName: sql.outputs.databaseName
    sqlAdminLogin: sqlAdminLogin
    sqlAdminPassword: sqlAdminPassword
    dpCertPassword: dpCertPassword
  }
}

module appService './modules/app-service.bicep' = {
  name: 'appService'
  scope: rg
  params: {
    location: location
    tags: tags
    appServicePlanId: appServicePlan.outputs.planId
    appServiceName: webAppName
    keyVaultName: keyVault.outputs.vaultName
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceId
    billingProvider: billingProvider
    stripeProMonthlyPriceId: stripeProMonthlyPriceId
    stripeProYearlyPriceId: stripeProYearlyPriceId
  }
}

module appServiceCustomDomain './modules/app-service-custom-domain.bicep' = {
  name: 'appServiceCustomDomain'
  scope: rg
  params: {
    appServiceName: webAppName
  }
  dependsOn: [
    appService
  ]
}

module roleAssignments './modules/role-assignments.bicep' = {
  name: 'roleAssignments'
  scope: rg
  params: {
    keyVaultName: keyVault.outputs.vaultName
    deployerObjectId: deployerObjectId
    appPrincipalId: appService.outputs.principalId
  }
}

output resourceGroupName string = rg.name
output appServicePlanName string = appServicePlanName
output webAppName string = webAppName
output sqlServerName string = sql.outputs.serverName
output sqlDatabaseName string = sql.outputs.databaseName
output keyVaultName string = keyVault.outputs.vaultName
output logAnalyticsWorkspaceId string = logAnalytics.outputs.workspaceId
