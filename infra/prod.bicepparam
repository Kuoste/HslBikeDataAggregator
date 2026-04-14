using './main.bicep'

param environmentName = 'prod'
param functionAppName = 'func-hsl-bike-data-aggregator-prod-flex'
param apimServiceName = 'apim-hsl-bike-data-aggregator-prod'
param corsAllowedOrigins = [
  'https://kuoste.github.io'
  'https://tmikuoste.github.io'
]
param pollIntervalCron = '0 */15 * * * *'
param snapshotHistoryLimit = 60
param historyProcessingCron = '0 0 2 * * *'
