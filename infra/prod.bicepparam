using './main.bicep'

param environmentName = 'prod'
param functionAppName = 'func-hsl-bike-data-aggregator-prod'
param corsAllowedOrigin = 'https://kuoste.github.io'
param pollIntervalCron = '0 */5 * * * *'
param snapshotHistoryLimit = 60
param historyProcessingCron = '0 0 2 * * *'
