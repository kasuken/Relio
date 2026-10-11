param location string
param tags object
param appServicePlanId string
param appServiceName string
param keyVaultName string
param logAnalyticsWorkspaceId string

@description('Hosted billing. Stripe needs the two Key Vault secrets stripe-api-key and stripe-webhook-signing-secret and both price ids (see docs/security/billing.md); None turns plans and limits off.')
@allowed([
  'None'
  'Stripe'
])
param billingProvider string = 'None'

@description('Stripe price id of Relio Pro billed monthly ($2 / month). Required when billingProvider is Stripe.')
param stripeProMonthlyPriceId string = ''

@description('Stripe price id of Relio Pro billed yearly ($12 / year). Required when billingProvider is Stripe.')
param stripeProYearlyPriceId string = ''

@description('Public origin of the site, used for canonical URLs and the Stripe return URLs.')
param publicOrigin string = 'https://www.relio.club'

var sqlConnectionStringReference = '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=sql-connection-string)'
var dataProtectionCertificatePasswordReference = '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=dp-cert-password)'
var stripeApiKeyReference = '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=stripe-api-key)'
var stripeWebhookSigningSecretReference = '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=stripe-webhook-signing-secret)'
var planPageUrl = '${publicOrigin}/Account/Manage/Plan'

// Only with Stripe: the app validates these at startup and refuses to start if one is missing.
var billingAppSettings = billingProvider == 'Stripe'
  ? [
      {
        name: 'Billing__ApiKey'
        value: stripeApiKeyReference
      }
      {
        name: 'Billing__WebhookSigningSecret'
        value: stripeWebhookSigningSecretReference
      }
      {
        name: 'Billing__ProMonthlyPriceId'
        value: stripeProMonthlyPriceId
      }
      {
        name: 'Billing__ProYearlyPriceId'
        value: stripeProYearlyPriceId
      }
      {
        name: 'Billing__CheckoutSuccessUrl'
        value: planPageUrl
      }
      {
        name: 'Billing__CheckoutCancelUrl'
        value: planPageUrl
      }
      {
        name: 'Billing__PortalReturnUrl'
        value: planPageUrl
      }
    ]
  : []

resource appService 'Microsoft.Web/sites@2026-08-01' = {
  name: appServiceName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    clientAffinityEnabled: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      webSocketsEnabled: true
      healthCheckPath: '/health/live'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true
      appSettings: concat([
        {
          name: 'ConnectionStrings__Relio'
          value: sqlConnectionStringReference
        }
        {
          name: 'Database__ApplyMigrationsOnStartup'
          value: 'true'
        }
        {
          name: 'DataProtection__ApplicationName'
          value: 'Relio'
        }
        {
          name: 'DataProtection__KeyRingPath'
          value: '/home/relio/keys'
        }
        {
          name: 'DataProtection__ProtectionMode'
          value: 'Certificate'
        }
        {
          name: 'DataProtection__Certificate__Path'
          value: '/home/relio/certificate.pfx'
        }
        {
          name: 'DataProtection__Certificate__Password'
          value: dataProtectionCertificatePasswordReference
        }
        {
          name: 'DataProtection__AutoGenerateIfMissing'
          value: 'true'
        }
        {
          name: 'Registration__Mode'
          value: 'Open'
        }
        {
          name: 'Email__Provider'
          value: 'None'
        }
        {
          name: 'Billing__Provider'
          value: billingProvider
        }
        {
          name: 'DemoData__Enabled'
          value: 'false'
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'SCM_DO_BUILD_DURING_DEPLOYMENT'
          value: 'true'
        }
        {
          name: 'ENABLE_ORYX_BUILD'
          value: 'true'
        }
        {
          name: 'ORYX_DISABLE_COMPRESSION'
          value: 'true'
        }
        {
          name: 'PROJECT'
          value: 'Relio.Web/Relio.Web.csproj'
        }
        {
          name: 'WEBSITES_CONTAINER_START_TIME_LIMIT'
          value: '1800'
        }
        {
          name: 'Seo__PublicOrigin'
          value: publicOrigin
        }
      ], billingAppSettings)
    }
  }
}

// Publishing credentials stay disabled outside the one-time code deployment step.
resource scmAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2023-12-01' = {
  parent: appService
  name: 'scm'
  properties: {
    allow: false
  }
}

// FTP basic auth is always disabled.
resource ftpAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2023-12-01' = {
  parent: appService
  name: 'ftp'
  properties: {
    allow: false
  }
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'app-service-diagnostics'
  scope: appService
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        category: 'AppServiceConsoleLogs'
        enabled: true
      }
      {
        category: 'AppServiceHTTPLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

output appServiceId string = appService.id
output principalId string = appService.identity.principalId
