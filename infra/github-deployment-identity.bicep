// GitHub Actions deployment identity (OIDC, no stored secret) for one GitHub environment.
//
// The defaults describe the production identity (id-relio-github-deploy, environment
// `production`, Website Contributor on the production web app). Staging uses its own identity in
// its own resource group, so a staging workflow run never holds any right on production:
//
//   az deployment group create -g rg-relio-staging -n relio-staging-github-identity \
//     --template-file infra/github-deployment-identity.bicep \
//     --parameters identityName=id-relio-github-deploy-staging githubEnvironment=staging \
//       githubSubjectPrefix="$(gh api repos/kasuken/Relio/actions/oidc/customization/sub --jq .sub_claim_prefix)" \
//       grantResourceGroupDeployment=true
//
// See docs/operations/staging.md.
targetScope = 'resourceGroup'

param location string = resourceGroup().location
param webAppName string = 'app-relio-prod-edff'
param githubRepository string = 'kasuken/Relio'
param githubEnvironment string = 'production'

param identityName string = 'id-relio-github-deploy'

param federatedCredentialName string = 'github-release-${githubEnvironment}'

@description('Start of the OIDC subject GitHub issues for this repository. It is the sub_claim_prefix reported by `gh api repos/<repo>/actions/oidc/customization/sub`; for a repository that uses immutable subjects it is not `repo:<owner>/<repo>`.')
param githubSubjectPrefix string = 'repo:${githubRepository}'

@description('Staging: let the identity deploy infrastructure into this resource group (Contributor, plus role assignment rights limited to Key Vault Secrets User/Officer) instead of only deploying code to an existing web app.')
param grantResourceGroupDeployment bool = false

var websiteContributorRoleDefinitionId = 'de139f84-1756-47ae-9be6-808fbbe84772'
var contributorRoleDefinitionId = 'b24988ac-6180-42dd-a87b-2ef71b8f8f2b'
var rbacAdministratorRoleDefinitionId = 'f58310d9-a9f6-439a-9e8d-f62e7b41a168'

// The RBAC Administrator may write or delete only Key Vault Secrets User (4633458b...) and
// Key Vault Secrets Officer (b86a8fe4...) assignments (a multi-line string cannot interpolate, so the ids are literal), so it cannot grant
// itself or anyone else anything broader (for example Owner).
var rbacAdministratorCondition = '''((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {4633458b-17de-408a-b874-0445c86b69e6, b86a8fe4-44ce-4948-aee5-eccb2c155cd7})) AND ((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {4633458b-17de-408a-b874-0445c86b69e6, b86a8fe4-44ce-4948-aee5-eccb2c155cd7}))'''

resource githubDeployIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: identityName
  location: location
}

resource githubFederatedCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = {
  parent: githubDeployIdentity
  name: federatedCredentialName
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: '${githubSubjectPrefix}:environment:${githubEnvironment}'
    audiences: [
      'api://AzureADTokenExchange'
    ]
  }
}

resource webApp 'Microsoft.Web/sites@2024-04-01' existing = if (!grantResourceGroupDeployment) {
  name: webAppName
}

// Production: code deployment to the one web app. Staging: Contributor on the resource group
// is a superset, and the web app does not exist yet when this identity is created.
resource webAppDeployRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!grantResourceGroupDeployment) {
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

resource resourceGroupContributorRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (grantResourceGroupDeployment) {
  name: guid(resourceGroup().id, githubDeployIdentity.id, contributorRoleDefinitionId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', contributorRoleDefinitionId)
    principalId: githubDeployIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource resourceGroupRbacAdministratorRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (grantResourceGroupDeployment) {
  name: guid(resourceGroup().id, githubDeployIdentity.id, rbacAdministratorRoleDefinitionId)
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      rbacAdministratorRoleDefinitionId
    )
    principalId: githubDeployIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    conditionVersion: '2.0'
    condition: rbacAdministratorCondition
  }
}

output clientId string = githubDeployIdentity.properties.clientId
output tenantId string = tenant().tenantId
output subscriptionId string = subscription().subscriptionId
