targetScope = 'resourceGroup'

param location string = resourceGroup().location
param webAppName string = 'app-relio-prod-edff'
param githubRepository string = 'kasuken/Relio'
param githubEnvironment string = 'production'

var identityName = 'id-relio-github-deploy'
var websiteContributorRoleDefinitionId = 'de139f84-1756-47ae-9be6-808fbbe84772'

resource githubDeployIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: identityName
  location: location
}

resource githubFederatedCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = {
  parent: githubDeployIdentity
  name: 'github-release-production'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${githubRepository}:environment:${githubEnvironment}'
    audiences: [
      'api://AzureADTokenExchange'
    ]
  }
}

resource webApp 'Microsoft.Web/sites@2024-04-01' existing = {
  name: webAppName
}

resource webAppDeployRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(webApp.id, githubDeployIdentity.id, websiteContributorRoleDefinitionId)
  scope: webApp
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      websiteContributorRoleDefinitionId
    )
    principalId: githubDeployIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

output clientId string = githubDeployIdentity.properties.clientId
output tenantId string = tenant().tenantId
output subscriptionId string = subscription().subscriptionId
