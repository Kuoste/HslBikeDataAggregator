using './main.bicep'

param environmentName = 'dev'
param functionAppName = 'func-hsl-bike-data-aggregator-dev'
param corsAllowedOrigin = 'https://kuoste.github.io'
param pollIntervalCron = '0 */5 * * * *'
param snapshotHistoryLimit = 60
