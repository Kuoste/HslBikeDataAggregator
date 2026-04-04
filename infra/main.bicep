targetScope = 'resourceGroup'

@description('Deployment environment name.')
@allowed([
  'dev'
  'prod'
])
param environmentName string

@description('Azure region for the deployment.')
param location string = resourceGroup().location

@description('Globally unique Azure Function App name.')
@minLength(2)
param functionAppName string

@description('Frontend origin allowed by Azure Functions CORS configuration.')
param corsAllowedOrigin string = 'https://kuoste.github.io'

@description('Cron expression used by the PollStations timer trigger.')
param pollIntervalCron string = '0 */5 * * * *'

@description('Number of snapshots retained in blob storage.')
@minValue(1)
param snapshotHistoryLimit int = 60

var appServicePlanName = '${functionAppName}-plan'
var applicationInsightsName = '${functionAppName}-appi'
var storageAccountName = take('st${toLower(replace(replace(functionAppName, '-', ''), '_', ''))}${uniqueString(resourceGroup().id)}', 24)
var contentShareName = take(toLower('${replace(replace(functionAppName, '-', ''), '_', '')}${uniqueString(functionAppName)}'), 63)
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
  tags: {
    'azd-env-name': environmentName
    environment: environmentName
    project: 'HslBikeDataAggregator'
  }
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    Flow_Type: 'Bluefield'
    Request_Source: 'rest'
  }
  tags: {
    'azd-env-name': environmentName
    environment: environmentName
    project: 'HslBikeDataAggregator'
  }
}

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  kind: 'functionapp'
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {
    reserved: false
  }
  tags: {
    'azd-env-name': environmentName
    environment: environmentName
    project: 'HslBikeDataAggregator'
  }
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      alwaysOn: false
      appSettings: [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: applicationInsights.properties.ConnectionString
        }
        {
          name: 'AzureWebJobsStorage'
          value: storageConnectionString
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'PollIntervalCron'
          value: pollIntervalCron
        }
        {
          name: 'SnapshotHistoryLimit'
          value: string(snapshotHistoryLimit)
        }
        {
          name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
          value: storageConnectionString
        }
        {
          name: 'WEBSITE_CONTENTSHARE'
          value: contentShareName
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
      ]
      cors: {
        allowedOrigins: [
          corsAllowedOrigin
        ]
        supportCredentials: false
      }
      ftpsState: 'Disabled'
      http20Enabled: true
      minTlsVersion: '1.2'
    }
  }
  tags: {
    'azd-env-name': environmentName
    environment: environmentName
    project: 'HslBikeDataAggregator'
  }
}

output functionAppName string = functionApp.name
output functionHostname string = 'https://${functionApp.properties.defaultHostName}'
output storageAccountName string = storageAccount.name
output applicationInsightsName string = applicationInsights.name
output managedIdentityPrincipalId string = functionApp.identity.principalId
