using './main.bicep'

param environmentName = 'dev'
param functionAppName = 'func-hsl-bike-data-aggregator-dev-flex'
param apimServiceName = 'apim-hsl-bike-data-aggregator-dev'
param corsAllowedOrigins = [
  'https://kuoste.github.io'
  'http://localhost:5000'
]
param pollIntervalCron = '0 */15 * * * *'
param snapshotHistoryLimit = 60
param historyProcessingCron = '0 0 2 * * *'
