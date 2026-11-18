// CLAIMFLOW: Azure infrastructure for the public demo.
//
// Creates: a Linux App Service plan (F1, free), a Web App running .NET 10, and an Azure SQL logical server
// with one database on the Azure SQL free offer. The app reaches the database with its system-assigned
// managed identity, so no password or secret exists anywhere.
//
// The app and the database can live in different regions: within this subscription's allowed regions,
// only France Central offers the SQL free tier, and it has no F1 App Service quota.
//
// Deploy (only after the cost has been reviewed and approved):
//   az deployment group create -g rg-claimflow -f infra/main.bicep -p infra/main.bicepparam

targetScope = 'resourceGroup'

@description('Region for the App Service plan and Web App (needs F1 quota).')
param location string = resourceGroup().location

@description('Region for the Azure SQL server and database (needs the Azure SQL free offer).')
param sqlLocation string = location

@description('Short lowercase prefix used in resource names.')
@minLength(3)
@maxLength(12)
param namePrefix string = 'claimflow'

@description('Entra ID object ID of the SQL administrator (the developer\'s own account).')
param sqlAdminObjectId string

@description('Entra ID sign-in name of the SQL administrator.')
param sqlAdminLogin string

@description('App Service plan SKU. F1 is free; B1 is the cheapest paid tier.')
@allowed(['F1', 'B1'])
param appServiceSku string = 'F1'

@description('Tags applied to every resource, for cost tracking.')
param tags object = {
  project: 'claimflow'
  environment: 'demo'
}

var suffix = uniqueString(resourceGroup().id)
var appName = '${namePrefix}-${suffix}'
var sqlServerName = '${namePrefix}-sql-${suffix}'
var databaseName = 'claimflow'

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${namePrefix}-plan'
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: appServiceSku
  }
  properties: {
    reserved: true // Linux
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: sqlServerName
  location: sqlLocation
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true // no SQL logins or passwords at all
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      tenantId: subscription().tenantId
      principalType: 'User'
    }
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: databaseName
  location: sqlLocation
  tags: tags
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    // Azure SQL free offer: 100 000 vCore seconds and 32 GB per month at no cost.
    // When the monthly allowance is used up the database pauses instead of billing.
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    // No autoPauseDelay: Azure refuses any value but its default for a free database with AutoPause.
    // It reports 60 minutes; measured, the database paused about 16 minutes after its last connection.
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368 // 32 GB
    requestedBackupStorageRedundancy: 'Local'
    zoneRedundant: false
  }
}

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: appName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: appServiceSku != 'F1' // not available on the free tier
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: null
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        // Lets the app see the real client IP behind App Service's front end (used for rate limiting).
        { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
        { name: 'Database__Provider', value: 'SqlServer' }
        // The app's identity can only read and write data; the SQL admin applies migrations (infra/DEPLOYMENT.md).
        // At startup the app only checks that no migration is pending.
        { name: 'Database__MigrateOnStartup', value: 'false' }
      ]
      connectionStrings: [
        {
          name: 'ClaimFlow'
          type: 'SQLAzure'
          // Not a secret: authentication uses the app's managed identity.
          connectionString: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${databaseName};Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connection Timeout=90'
        }
      ]
    }
  }
}

// The F1 plan cannot join a virtual network or use private endpoints, so the database is reached over its
// public endpoint (TLS 1.2+, Entra ID only). The firewall admits only the Web App's own outbound addresses,
// not "all Azure services". If App Service ever moves the app to other addresses, re-run this template.
module sqlFirewall 'modules/sqlFirewall.bicep' = {
  name: 'sql-firewall'
  params: {
    sqlServerName: sqlServer.name
    outboundIpAddresses: app.properties.possibleOutboundIpAddresses
  }
}

// Deployments use Entra ID (GitHub OIDC), so basic-auth publishing credentials are switched off.
resource scmBasicAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: app
  name: 'scm'
  properties: {
    allow: false
  }
}

resource ftpBasicAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: app
  name: 'ftp'
  properties: {
    allow: false
  }
}

output appName string = app.name
output appUrl string = 'https://${app.properties.defaultHostName}'
output appPrincipalId string = app.identity.principalId
output sqlServerName string = sqlServer.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = databaseName
output sqlFirewallRuleCount int = sqlFirewall.outputs.ruleCount
