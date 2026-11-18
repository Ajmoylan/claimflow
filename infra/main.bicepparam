using './main.bicep'

// App and database are in different regions: of this subscription's allowed regions, only France Central
// offers the Azure SQL free tier, and France Central has no F1 App Service quota.
param location = 'belgiumcentral'
param sqlLocation = 'francecentral'
param namePrefix = 'claimflow'
param appServiceSku = 'F1'

// Fill these in at deploy time (they identify the SQL administrator; they are not secrets):
//   az ad signed-in-user show --query "{id:id, upn:userPrincipalName}"
param sqlAdminObjectId = readEnvironmentVariable('CLAIMFLOW_SQL_ADMIN_OBJECT_ID', '')
param sqlAdminLogin = readEnvironmentVariable('CLAIMFLOW_SQL_ADMIN_LOGIN', '')
