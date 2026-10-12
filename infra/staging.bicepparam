using './staging.bicep'

// Must match the env block of .github/workflows/staging.yml.
param namingSuffix = 'edff'

// Secrets come from the GitHub `staging` environment and are never committed
// (see docs/operations/staging.md).
param sqlAdminPassword = readEnvironmentVariable('RELIO_SQL_ADMIN_PASSWORD')
param dpCertPassword = readEnvironmentVariable('RELIO_DP_CERT_PASSWORD')
param deployerObjectId = readEnvironmentVariable('RELIO_DEPLOYER_OBJECT_ID', '')
