using './main.bicep'

param environmentName = 'dev'
param functionAppName = 'func-hsl-bike-data-aggregator-dev-flex'
param corsAllowedOrigin = 'https://kuoste.github.io'
param pollIntervalCron = '0 */15 * * * *'
param snapshotHistoryLimit = 60
param historyProcessingCron = '0 0 2 * * *'
