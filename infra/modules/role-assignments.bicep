param keyVaultName string
param deployerObjectId string
param appPrincipalId string

resource keyVault 'Microsoft.KeyVault/vaults@2026-05-15' existing = {
  name: keyVaultName
}

var keyVaultSecretsOfficerRoleDefinitionId = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
var keyVaultSecretsUserRoleDefinitionId = '4633458b-17de-408a-b874-0445c86b69e6'

resource deployerKeyVaultRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, deployerObjectId, keyVaultSecretsOfficerRoleDefinitionId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      keyVaultSecretsOfficerRoleDefinitionId
    )
    principalId: deployerObjectId
    principalType: 'User'
  }
}

resource appKeyVaultRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, appPrincipalId, keyVaultSecretsUserRoleDefinitionId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      keyVaultSecretsUserRoleDefinitionId
    )
    principalId: appPrincipalId
    principalType: 'ServicePrincipal'
  }
}
