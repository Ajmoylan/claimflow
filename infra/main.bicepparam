using './main.bicep'

param location = 'francecentral'
param namePrefix = 'claimflow'
param appServiceSku = 'F1'

// Fill these in at deploy time (they identify the SQL administrator; they are not secrets):
//   az ad signed-in-user show --query "{id:id, upn:userPrincipalName}"
param sqlAdminObjectId = readEnvironmentVariable('CLAIMFLOW_SQL_ADMIN_OBJECT_ID', '')
param sqlAdminLogin = readEnvironmentVariable('CLAIMFLOW_SQL_ADMIN_LOGIN', '')
