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
param pollIntervalCron string = '0 */15 * * * *'

@description('Number of snapshots retained in blob storage.')
@minValue(1)
param snapshotHistoryLimit int = 60

@description('Cron expression used by the ProcessStationHistory timer trigger.')
param historyProcessingCron string = '0 0 2 * * *'

var appServicePlanName = '${functionAppName}-plan'
var applicationInsightsName = '${functionAppName}-appi'
var storageAccountName = take('st${toLower(replace(replace(functionAppName, '-', ''), '_', ''))}${uniqueString(resourceGroup().id)}', 24)
var deploymentStorageContainerName = 'deployment-packages'
var deploymentStorageContainerUrl = 'https://${storageAccount.name}.blob.${environment().suffixes.storage}/${deploymentStorageContainerName}'
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

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  name: 'default'
  parent: storageAccount
}

resource deploymentStorageContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: deploymentStorageContainerName
  parent: blobService
  properties: {
    publicAccess: 'None'
  }
}

resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: appServicePlanName
  location: location
  kind: 'functionapp'
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true
  }
  tags: {
    'azd-env-name': environmentName
    environment: environmentName
    project: 'HslBikeDataAggregator'
  }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    clientAffinityEnabled: false
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: deploymentStorageContainerUrl
          authentication: {
            type: 'StorageAccountConnectionString'
            storageAccountConnectionStringName: 'AzureWebJobsStorage'
          }
        }
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
      scaleAndConcurrency: {
        instanceMemoryMB: 512
      }
    }
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
          name: 'HistoryProcessingCron'
          value: historyProcessingCron
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
