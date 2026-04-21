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

@description('Globally unique Azure API Management service name.')
@minLength(2)
param apimServiceName string

@description('Frontend origins allowed by the API Management CORS policy.')
param corsAllowedOrigins array

@description('Cron expression used by the PollStations timer trigger.')
param pollIntervalCron string = '0 */15 * * * *'

@description('Number of snapshots retained in blob storage.')
@minValue(1)
param snapshotHistoryLimit int = 60

@description('Cron expression used by the ProcessStationHistory timer trigger.')
param historyProcessingCron string = '0 0 2 * * *'

var managedIdentityName = '${functionAppName}-id'
var appServicePlanName = '${functionAppName}-plan'
var applicationInsightsName = '${functionAppName}-appi'
var logAnalyticsWorkspaceName = '${functionAppName}-law'
var storageAccountName = take('st${toLower(replace(replace(functionAppName, '-', ''), '_', ''))}${uniqueString(resourceGroup().id)}', 24)
var deploymentStorageContainerName = 'deployment-packages'
var deploymentStorageContainerUrl = 'https://${storageAccount.name}.blob.${environment().suffixes.storage}/${deploymentStorageContainerName}'
var apimCorsAllowedOriginsXml = join(map(corsAllowedOrigins, origin => '        <origin>${origin}</origin>'), '\n')

resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: managedIdentityName
  location: location
  tags: {
    'azd-env-name': environmentName
    environment: environmentName
    project: 'HslBikeDataAggregator'
  }
}

// Role definition IDs used in Azure
var roleDefinitionIds = {
  storageBlobDataOwner: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  storageQueueDataContributor: '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
}

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
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
  tags: {
    'azd-env-name': environmentName
    environment: environmentName
    project: 'HslBikeDataAggregator'
  }
}

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
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
    WorkspaceResourceId: logAnalyticsWorkspace.id
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
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentity.id}': {}
    }
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
            type: 'UserAssignedIdentity'
            userAssignedIdentityResourceId: managedIdentity.id
          }
        }
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 100
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
          name: 'AzureWebJobsStorage__accountName'
          value: storageAccount.name
        }
        {
          name: 'AzureWebJobsStorage__clientId'
          value: managedIdentity.properties.clientId
        }
        {
          name: 'AzureWebJobsStorage__credential'
          value: 'managedidentity'
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

// Role assignments for Managed Identity storage access
resource storageBlobDataOwnerRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, managedIdentity.id, roleDefinitionIds.storageBlobDataOwner)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      roleDefinitionIds.storageBlobDataOwner
    )
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource storageQueueDataContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, managedIdentity.id, roleDefinitionIds.storageQueueDataContributor)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      roleDefinitionIds.storageQueueDataContributor
    )
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Storage account diagnostic logging
resource storageDiagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: '${storageAccountName}-blob-logs'
  scope: blobService
  properties: {
    workspaceId: logAnalyticsWorkspace.id
    logs: [
      {
        category: 'StorageWrite'
        enabled: true
      }
      {
        category: 'StorageRead'
        enabled: true
      }
      {
        category: 'StorageDelete'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'Transaction'
        enabled: true
      }
    ]
  }
}

output functionAppName string = functionApp.name
output functionHostname string = 'https://${functionApp.properties.defaultHostName}'
output storageAccountName string = storageAccount.name
output applicationInsightsName string = applicationInsights.name
output logAnalyticsWorkspaceName string = logAnalyticsWorkspace.name
output managedIdentityPrincipalId string = managedIdentity.properties.principalId
output managedIdentityClientId string = managedIdentity.properties.clientId

// ---------------------------------------------------------------------------
// API Management — Consumption tier gateway
// ---------------------------------------------------------------------------

resource apimService 'Microsoft.ApiManagement/service@2024-05-01' = {
  name: apimServiceName
  location: location
  sku: {
    name: 'Consumption'
    capacity: 0
  }
  properties: {
    publisherEmail: 'noreply@${apimServiceName}.azure-api.net'
    publisherName: 'HslBikeDataAggregator'
  }
  tags: {
    'azd-env-name': environmentName
    environment: environmentName
    project: 'HslBikeDataAggregator'
  }
}

// Store the auto-generated Function App host key so APIM can authenticate.
// listKeys must target the host child resource, not the site itself.
// The API version is derived from the functionApp resource declaration so it
// stays in sync automatically when the resource API version is updated.
var functionHostKey = listKeys('${functionApp.id}/host/default', functionApp.apiVersion).functionKeys.default

resource apimFunctionKeyNamedValue 'Microsoft.ApiManagement/service/namedValues@2024-05-01' = {
  name: 'function-host-key'
  parent: apimService
  properties: {
    displayName: 'function-host-key'
    value: functionHostKey
    secret: true
  }
}

resource apimApi 'Microsoft.ApiManagement/service/apis@2024-05-01' = {
  name: 'hsl-bike-api'
  parent: apimService
  properties: {
    displayName: 'HSL Bike Data API'
    path: 'api'
    protocols: [
      'https'
    ]
    subscriptionRequired: false
    serviceUrl: 'https://${functionApp.properties.defaultHostName}/api'
  }
}

// Inbound policy applied to all operations: function key injection, CORS,
// global rate limiting and response caching.
// Note: Consumption tier only supports the basic rate-limit policy.
// The quota policy is restricted to product scope and by-key variants
// require Developer tier or above.
resource apimApiPolicy 'Microsoft.ApiManagement/service/apis/policies@2024-05-01' = {
  name: 'policy'
  parent: apimApi
  properties: {
    format: 'xml'
    value: '''
<policies>
  <inbound>
    <base />
    <set-header name="x-functions-key" exists-action="override">
      <value>{{function-host-key}}</value>
    </set-header>
    <cors allow-credentials="false">
      <allowed-origins>
${apimCorsAllowedOriginsXml}
      </allowed-origins>
      <allowed-methods>
        <method>GET</method>
      </allowed-methods>
      <allowed-headers>
        <header>*</header>
      </allowed-headers>
    </cors>
    <rate-limit calls="200" renewal-period="60" />
    <cache-lookup vary-by-developer="false" vary-by-developer-groups="false" />
  </inbound>
  <backend>
    <base />
  </backend>
  <outbound>
    <base />
    <cache-store duration="30" />
  </outbound>
  <on-error>
    <base />
  </on-error>
</policies>
'''
  }
  dependsOn: [
    apimFunctionKeyNamedValue
  ]
}

// Override cache duration for the monthly station statistics endpoint.
resource apimStationStatisticsCachePolicyFragment 'Microsoft.ApiManagement/service/apis/operations/policies@2024-05-01' = {
  name: 'policy'
  parent: apimGetStationStatistics
  properties: {
    format: 'xml'
    value: '''
<policies>
  <inbound>
    <base />
    <cache-lookup vary-by-developer="false" vary-by-developer-groups="false" />
  </inbound>
  <backend>
    <base />
  </backend>
  <outbound>
    <base />
    <cache-store duration="3600" />
  </outbound>
  <on-error>
    <base />
  </on-error>
</policies>
'''
  }
}


// API operations — one per Function App HTTP endpoint.
resource apimGetStations 'Microsoft.ApiManagement/service/apis/operations@2024-05-01' = {
  name: 'get-stations'
  parent: apimApi
  properties: {
    displayName: 'Get stations'
    method: 'GET'
    urlTemplate: '/stations'
  }
}

resource apimGetSnapshots 'Microsoft.ApiManagement/service/apis/operations@2024-05-01' = {
  name: 'get-snapshots'
  parent: apimApi
  properties: {
    displayName: 'Get snapshots'
    method: 'GET'
    urlTemplate: '/snapshots'
  }
}

resource apimGetStationStatistics 'Microsoft.ApiManagement/service/apis/operations@2024-05-01' = {
  name: 'get-station-statistics'
  parent: apimApi
  properties: {
    displayName: 'Get station statistics'
    method: 'GET'
    urlTemplate: '/stations/{stationId}/statistics'
    templateParameters: [
      {
        name: 'stationId'
        required: true
        type: 'string'
      }
    ]
  }
}

output apimGatewayUrl string = apimService.properties.gatewayUrl
