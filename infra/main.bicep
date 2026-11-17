// CLAIMFLOW: Azure infrastructure for the public demo.
//
// Creates: a Linux App Service plan (F1, free), a Web App running .NET 10, and an Azure SQL logical server
// with one database on the Azure SQL free offer. The app reaches the database with its system-assigned
// managed identity, so no password or secret exists anywhere.
//
// Deploy (only after the cost has been reviewed and approved):
//   az deployment group create -g rg-claimflow -f infra/main.bicep -p infra/main.bicepparam

targetScope = 'resourceGroup'

@description('Azure region. The subscription policy only allows a few regions; Azure SQL provisioning was available in francecentral.')
param location string = resourceGroup().location

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

var suffix = uniqueString(resourceGroup().id)
var appName = '${namePrefix}-${suffix}'
var sqlServerName = '${namePrefix}-sql-${suffix}'
var databaseName = 'claimflow'

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${namePrefix}-plan'
  location: location
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
  location: location
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

// The F1 plan cannot join a virtual network, so the server accepts connections from Azure services.
// Access still requires an Entra ID identity that has been granted a database user.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: databaseName
  location: location
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
    autoPauseDelay: 60
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368 // 32 GB
    requestedBackupStorageRedundancy: 'Local'
    zoneRedundant: false
  }
}

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: appName
  location: location
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
        { name: 'Database__MigrateOnStartup', value: 'true' }
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
