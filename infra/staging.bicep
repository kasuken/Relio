// Staging environment for Relio: the same modules as production (main.bicep) in their own
// resource group, with its own SQL server, Key Vault and Log Analytics workspace, so a staging
// deployment can never read or change production. Deployed by .github/workflows/staging.yml;
// operator guide in docs/operations/staging.md.
//
// Keep in step with main.bicep: a new module parameter there needs a decision here. Not shared
// with production on purpose: names, the resource group, the custom domain (staging stays on the
// *.azurewebsites.net platform certificate) and the secrets.
targetScope = 'resourceGroup'

param location string = resourceGroup().location

@description('Suffix of the globally unique names (web app, SQL server, Key Vault). Change it together with the env block of .github/workflows/staging.yml.')
param namingSuffix string = 'edff'

param sqlAdminLogin string = 'relioadmin'

@secure()
param sqlAdminPassword string

@secure()
param dpCertPassword string

@description('Object id of a person who may manage Key Vault secrets (Key Vault Secrets Officer). Optional.')
param deployerObjectId string = ''

@description('Hosted billing: None (no plans or limits) or Stripe. Off in staging unless a change says why; see docs/security/billing.md.')
@allowed([
  'None'
  'Stripe'
])
param billingProvider string = 'None'

@description('Stripe price id of Relio Pro billed monthly. Required when billingProvider is Stripe.')
param stripeProMonthlyPriceId string = ''

@description('Stripe price id of Relio Pro billed yearly. Required when billingProvider is Stripe.')
param stripeProYearlyPriceId string = ''

var appServicePlanName = 'asp-relio-stg-${namingSuffix}'
var webAppName = 'app-relio-stg-${namingSuffix}'
var sqlServerName = 'sql-relio-stg-${namingSuffix}'
var sqlDatabaseName = 'relio'
var keyVaultName = 'kv-relio-stg-${namingSuffix}'
var logAnalyticsWorkspaceName = 'log-relio-stg-${namingSuffix}'
var publicOrigin = 'https://${webAppName}.azurewebsites.net'

// Static values only: a changing tag (a timestamp) would make every what-if show a modification.
var tags = {
  environment: 'staging'
  application: 'relio'
  'managed-by': 'github-actions/staging.yml'
}

module logAnalytics './modules/log-analytics.bicep' = {
  name: 'logAnalytics'
  params: {
    location: location
    tags: tags
    workspaceName: logAnalyticsWorkspaceName
  }
}

module appServicePlan './modules/app-service-plan.bicep' = {
  name: 'appServicePlan'
  params: {
    location: location
    tags: tags
    planName: appServicePlanName
  }
}

module sql './modules/sql.bicep' = {
  name: 'sql'
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
    publicOrigin: publicOrigin
    aspnetcoreEnvironment: 'Staging'
    // The first account is always allowed and becomes the administrator; everyone after needs an invitation.
    registrationMode: 'InviteOnly'
    dataProtectionApplicationName: 'Relio-Staging'
    // The workflow deploys `dotnet publish` output; nothing to build on the server.
    buildDuringDeployment: false
  }
}

module roleAssignments './modules/role-assignments.bicep' = {
  name: 'roleAssignments'
  params: {
    keyVaultName: keyVault.outputs.vaultName
    deployerObjectId: deployerObjectId
    appPrincipalId: appService.outputs.principalId
  }
}

output webAppName string = webAppName
output defaultHostName string = appService.outputs.defaultHostName
output sqlServerName string = sql.outputs.serverName
output sqlDatabaseName string = sql.outputs.databaseName
output keyVaultName string = keyVault.outputs.vaultName
output logAnalyticsWorkspaceId string = logAnalytics.outputs.workspaceId
