// Read-only identity for .github/workflows/hosted-backup-check.yml (REL-005). Deploy it manually,
// like github-deployment-identity.bicep, only if you want the scheduled hosted backup check:
//
//   az deployment group create -g rg-relio-prod -f infra/github-backup-monitor-identity.bicep
//
// It gets the Reader role on the resource group and nothing else, is separate from the deployment
// identity, and is tied to the main branch (not the approval-gated production environment).
targetScope = 'resourceGroup'

param location string = resourceGroup().location
param githubRepository string = 'kasuken/Relio'
param githubBranch string = 'main'

var identityName = 'id-relio-github-backup-monitor'
var readerRoleDefinitionId = 'acdd72a7-3385-48ef-bd42-f606fba81ae7'

resource monitorIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: identityName
  location: location
}

resource federatedCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = {
  parent: monitorIdentity
  name: 'github-backup-monitor-main'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${githubRepository}:ref:refs/heads/${githubBranch}'
    audiences: [
      'api://AzureADTokenExchange'
    ]
  }
}

resource readerRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, monitorIdentity.id, readerRoleDefinitionId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', readerRoleDefinitionId)
    principalId: monitorIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

output clientId string = monitorIdentity.properties.clientId
output tenantId string = tenant().tenantId
output subscriptionId string = subscription().subscriptionId
