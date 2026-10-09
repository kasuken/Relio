param appServiceName string

resource appService 'Microsoft.Web/sites@2026-08-01' existing = {
  name: appServiceName
}

resource relioClubManagedCertificate 'Microsoft.Web/certificates@2026-08-01' existing = {
  name: 'www.relio.club'
}

resource relioClubHostname 'Microsoft.Web/sites/hostNameBindings@2026-08-01' = {
  parent: appService
  name: 'www.relio.club'
  properties: {
    siteName: appService.name
    hostNameType: 'Verified'
    customHostNameDnsRecordType: 'CName'
    sslState: 'SniEnabled'
    thumbprint: relioClubManagedCertificate.properties.thumbprint
  }
}
