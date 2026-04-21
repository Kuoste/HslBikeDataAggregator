namespace HslBikeDataAggregator.Tests.Configuration;

public sealed class DeploymentWorkflowConfigurationTests
{
    [Theory]
    [InlineData("infra", "main.bicep")]
    [InlineData("infra", "dev.bicepparam")]
    [InlineData("infra", "prod.bicepparam")]
    [InlineData(".github", "workflows", "deploy-dev.yml")]
    [InlineData(".github", "workflows", "deploy-prod.yml")]
    public void DeploymentScaffoldFile_Exists(params string[] parts)
    {
        var path = GetRepositoryFilePath(parts);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task CiWorkflow_ValidatesInfrastructure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var yaml = await File.ReadAllTextAsync(GetRepositoryFilePath(".github", "workflows", "ci.yml"), cancellationToken);

        Assert.Contains("validate-infrastructure:", yaml, StringComparison.Ordinal);
        Assert.Contains("az bicep install", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeployDevWorkflow_TargetsDevEnvironment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var yaml = await File.ReadAllTextAsync(GetRepositoryFilePath(".github", "workflows", "deploy-dev.yml"), cancellationToken);

        Assert.Contains("environment: dev", yaml, StringComparison.Ordinal);
        Assert.Contains("--parameters infra/dev.bicepparam", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeployProdWorkflow_TargetsProdEnvironment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var yaml = await File.ReadAllTextAsync(GetRepositoryFilePath(".github", "workflows", "deploy-prod.yml"), cancellationToken);

        Assert.Contains("environment: prod", yaml, StringComparison.Ordinal);
        Assert.Contains("--parameters infra/prod.bicepparam", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeployWorkflows_ContainRecommendedDefaultResourceNames()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var devYaml = await File.ReadAllTextAsync(GetRepositoryFilePath(".github", "workflows", "deploy-dev.yml"), cancellationToken);
        var prodYaml = await File.ReadAllTextAsync(GetRepositoryFilePath(".github", "workflows", "deploy-prod.yml"), cancellationToken);

        Assert.Contains("rg-hsl-bike-data-aggregator-dev", devYaml, StringComparison.Ordinal);
        Assert.Contains("func-hsl-bike-data-aggregator-dev-flex", devYaml, StringComparison.Ordinal);
        Assert.Contains("rg-hsl-bike-data-aggregator-prod", prodYaml, StringComparison.Ordinal);
        Assert.Contains("func-hsl-bike-data-aggregator-prod-flex", prodYaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeployWorkflows_ConfigureHistoryProcessingSettings()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var devYaml = await File.ReadAllTextAsync(GetRepositoryFilePath(".github", "workflows", "deploy-dev.yml"), cancellationToken);
        var prodYaml = await File.ReadAllTextAsync(GetRepositoryFilePath(".github", "workflows", "deploy-prod.yml"), cancellationToken);

        Assert.Contains("HistoryProcessingCron", devYaml, StringComparison.Ordinal);
        Assert.DoesNotContain("HistoryProcessing__TripHistoryUrls__0", devYaml, StringComparison.Ordinal);
        Assert.Contains("HistoryProcessingCron", prodYaml, StringComparison.Ordinal);
        Assert.DoesNotContain("HistoryProcessing__TripHistoryUrls__0", prodYaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BicepParameterFiles_ContainRecommendedFunctionAppNames()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var devParameters = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "dev.bicepparam"), cancellationToken);
        var prodParameters = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "prod.bicepparam"), cancellationToken);

        Assert.Contains("func-hsl-bike-data-aggregator-dev-flex", devParameters, StringComparison.Ordinal);
        Assert.Contains("func-hsl-bike-data-aggregator-prod-flex", prodParameters, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InfrastructureParameters_ContainHistoryProcessingCronDefaults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mainBicep = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "main.bicep"), cancellationToken);
        var devBicepParameters = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "dev.bicepparam"), cancellationToken);
        var prodBicepParameters = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "prod.bicepparam"), cancellationToken);

        Assert.Contains("param historyProcessingCron string", mainBicep, StringComparison.Ordinal);
        Assert.Contains("name: 'HistoryProcessingCron'", mainBicep, StringComparison.Ordinal);
        Assert.Contains("param historyProcessingCron = '0 0 2 * * *'", devBicepParameters, StringComparison.Ordinal);
        Assert.Contains("param historyProcessingCron = '0 0 2 * * *'", prodBicepParameters, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InfrastructureParameters_ContainReducedPollIntervalDefaults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mainBicep = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "main.bicep"), cancellationToken);
        var devBicepParameters = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "dev.bicepparam"), cancellationToken);
        var prodBicepParameters = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "prod.bicepparam"), cancellationToken);

        Assert.Contains("param pollIntervalCron string = '0 */15 * * * *'", mainBicep, StringComparison.Ordinal);
        Assert.Contains("param pollIntervalCron = '0 */15 * * * *'", devBicepParameters, StringComparison.Ordinal);
        Assert.Contains("param pollIntervalCron = '0 */15 * * * *'", prodBicepParameters, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Infrastructure_UsesFlexConsumptionPlan()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mainBicep = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "main.bicep"), cancellationToken);

        Assert.Contains("tier: 'FlexConsumption'", mainBicep, StringComparison.Ordinal);
        Assert.Contains("name: 'FC1'", mainBicep, StringComparison.Ordinal);
        Assert.Contains("kind: 'functionapp,linux'", mainBicep, StringComparison.Ordinal);
        Assert.Contains("functionAppConfig:", mainBicep, StringComparison.Ordinal);
        Assert.Contains("maximumInstanceCount:", mainBicep, StringComparison.Ordinal);
        Assert.Contains("deploymentStorageContainerName = 'deployment-packages'", mainBicep, StringComparison.Ordinal);
        Assert.DoesNotContain("WEBSITE_CONTENTSHARE", mainBicep, StringComparison.Ordinal);
        Assert.DoesNotContain("FUNCTIONS_WORKER_RUNTIME", mainBicep, StringComparison.Ordinal);
        Assert.DoesNotContain("FUNCTIONS_EXTENSION_VERSION", mainBicep, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Infrastructure_ConfiguresCorsAsArrayWithPlatformOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mainBicep = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "main.bicep"), cancellationToken);
        var devBicepParameters = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "dev.bicepparam"), cancellationToken);
        var prodBicepParameters = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "prod.bicepparam"), cancellationToken);

        Assert.Contains("param corsAllowedOrigins array", mainBicep, StringComparison.Ordinal);
        Assert.Contains("var apimCorsAllowedOriginsXml = join(map(corsAllowedOrigins", mainBicep, StringComparison.Ordinal);
        Assert.Contains("${apimCorsAllowedOriginsXml}", mainBicep, StringComparison.Ordinal);
        Assert.DoesNotContain("allowedOrigins: corsAllowedOrigins", mainBicep, StringComparison.Ordinal);

        Assert.Contains("param corsAllowedOrigins", devBicepParameters, StringComparison.Ordinal);
        Assert.Contains("http://localhost:5291", devBicepParameters, StringComparison.Ordinal);
        Assert.Contains("param corsAllowedOrigins", prodBicepParameters, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", prodBicepParameters, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeployWorkflows_PublishFlexConsumptionPackages()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var devYaml = await File.ReadAllTextAsync(GetRepositoryFilePath(".github", "workflows", "deploy-dev.yml"), cancellationToken);
        var prodYaml = await File.ReadAllTextAsync(GetRepositoryFilePath(".github", "workflows", "deploy-prod.yml"), cancellationToken);

        Assert.Contains("released-package.zip", devYaml, StringComparison.Ordinal);
        Assert.Contains("sku: flexconsumption", devYaml, StringComparison.Ordinal);
        Assert.Contains("released-package.zip", prodYaml, StringComparison.Ordinal);
        Assert.Contains("sku: flexconsumption", prodYaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AzurePlan_IsReadyForExecutionOrValidation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var markdown = await File.ReadAllTextAsync(GetRepositoryFilePath(".azure", "plan.md"), cancellationToken);

        Assert.Contains("## Status", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Infrastructure_UsesManagedIdentityStorage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mainBicep = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "main.bicep"), cancellationToken);

        Assert.Contains("allowSharedKeyAccess: false", mainBicep, StringComparison.Ordinal);
        Assert.Contains("defaultToOAuthAuthentication: true", mainBicep, StringComparison.Ordinal);
        Assert.Contains("AzureWebJobsStorage__accountName", mainBicep, StringComparison.Ordinal);
        Assert.DoesNotContain("AzureWebJobsStorage'", mainBicep, StringComparison.Ordinal);
        Assert.Contains("type: 'UserAssignedIdentity'", mainBicep, StringComparison.Ordinal);
        Assert.Contains("type: 'UserAssigned'", mainBicep, StringComparison.Ordinal);
        Assert.Contains("AzureWebJobsStorage__clientId", mainBicep, StringComparison.Ordinal);
        Assert.Contains("AzureWebJobsStorage__credential", mainBicep, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Infrastructure_AssignsStorageRbacRoles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mainBicep = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "main.bicep"), cancellationToken);

        Assert.Contains("b7e6dc6d-f1e8-4753-8033-0f276bb0955b", mainBicep, StringComparison.Ordinal);
        Assert.Contains("974c5e8b-45b9-4653-ba55-5f855dd0fb88", mainBicep, StringComparison.Ordinal);
        Assert.Contains("storageBlobDataOwnerRole", mainBicep, StringComparison.Ordinal);
        Assert.Contains("storageQueueDataContributorRole", mainBicep, StringComparison.Ordinal);
        Assert.Contains("principalType: 'ServicePrincipal'", mainBicep, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Infrastructure_HasLogAnalyticsBackedApplicationInsights()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mainBicep = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "main.bicep"), cancellationToken);

        Assert.Contains("Microsoft.OperationalInsights/workspaces", mainBicep, StringComparison.Ordinal);
        Assert.Contains("WorkspaceResourceId: logAnalyticsWorkspace.id", mainBicep, StringComparison.Ordinal);
        Assert.Contains("logAnalyticsWorkspaceName", mainBicep, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Infrastructure_ConfiguresStorageDiagnosticSettings()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mainBicep = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "main.bicep"), cancellationToken);

        Assert.Contains("Microsoft.Insights/diagnosticSettings", mainBicep, StringComparison.Ordinal);
        Assert.Contains("StorageWrite", mainBicep, StringComparison.Ordinal);
        Assert.Contains("StorageRead", mainBicep, StringComparison.Ordinal);
        Assert.Contains("StorageDelete", mainBicep, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeployWorkflows_PinActionsToCommitShas()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var devYaml = await File.ReadAllTextAsync(GetRepositoryFilePath(".github", "workflows", "deploy-dev.yml"), cancellationToken);
        var prodYaml = await File.ReadAllTextAsync(GetRepositoryFilePath(".github", "workflows", "deploy-prod.yml"), cancellationToken);
        var ciYaml = await File.ReadAllTextAsync(GetRepositoryFilePath(".github", "workflows", "ci.yml"), cancellationToken);

        var workflows = new[] { devYaml, prodYaml, ciYaml };
        foreach (var yaml in workflows)
        {
            Assert.DoesNotMatch(@"uses:\s+\S+@v\d", yaml);
        }
    }

    [Fact]
    public async Task DeployProdWorkflow_RestrictsGitRefToMain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var prodYaml = await File.ReadAllTextAsync(GetRepositoryFilePath(".github", "workflows", "deploy-prod.yml"), cancellationToken);

        Assert.Contains("Validate deployment ref", prodYaml, StringComparison.Ordinal);
        Assert.Contains("Production deployments are restricted to the main branch", prodYaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Dependabot_ConfigurationExists()
    {
        var path = GetRepositoryFilePath(".github", "dependabot.yml");

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Dependabot_MonitorsNuGetAndGitHubActions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dependabotYaml = await File.ReadAllTextAsync(GetRepositoryFilePath(".github", "dependabot.yml"), cancellationToken);

        Assert.Contains("package-ecosystem: nuget", dependabotYaml, StringComparison.Ordinal);
        Assert.Contains("package-ecosystem: github-actions", dependabotYaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Infrastructure_ProvisionApimConsumptionInstance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mainBicep = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "main.bicep"), cancellationToken);

        Assert.Contains("Microsoft.ApiManagement/service", mainBicep, StringComparison.Ordinal);
        Assert.Contains("name: 'Consumption'", mainBicep, StringComparison.Ordinal);
        Assert.Contains("param apimServiceName string", mainBicep, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Infrastructure_ApimImportsFunctionAppEndpoints()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mainBicep = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "main.bicep"), cancellationToken);

        Assert.Contains("Microsoft.ApiManagement/service/apis", mainBicep, StringComparison.Ordinal);
        Assert.Contains("Microsoft.ApiManagement/service/apis/operations", mainBicep, StringComparison.Ordinal);
        Assert.Contains("/stations", mainBicep, StringComparison.Ordinal);
        Assert.Contains("/snapshots", mainBicep, StringComparison.Ordinal);
        Assert.Contains("/stations/{stationId}/statistics", mainBicep, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Infrastructure_ApimInjectsFunctionHostKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mainBicep = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "main.bicep"), cancellationToken);

        Assert.Contains("listKeys('${functionApp.id}/host/default'", mainBicep, StringComparison.Ordinal);
        Assert.Contains("function-host-key", mainBicep, StringComparison.Ordinal);
        Assert.Contains("x-functions-key", mainBicep, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Infrastructure_ApimConfiguresRateLimiting()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mainBicep = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "main.bicep"), cancellationToken);

        Assert.Contains("rate-limit calls=", mainBicep, StringComparison.Ordinal);
        Assert.DoesNotContain("rate-limit-by-key", mainBicep, StringComparison.Ordinal);
        Assert.DoesNotContain("<quota", mainBicep, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Infrastructure_ApimConfiguresResponseCaching()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mainBicep = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "main.bicep"), cancellationToken);

        Assert.Contains("cache-lookup", mainBicep, StringComparison.Ordinal);
        Assert.Contains("cache-store", mainBicep, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Infrastructure_ApimOverridesStationStatisticsCacheDuration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mainBicep = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "main.bicep"), cancellationToken);

        Assert.Contains("parent: apimGetStationStatistics", mainBicep, StringComparison.Ordinal);
        Assert.Contains("cache-store duration=\"3600\"", mainBicep, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Infrastructure_OutputsApimGatewayUrl()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mainBicep = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "main.bicep"), cancellationToken);

        Assert.Contains("output apimGatewayUrl string", mainBicep, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InfrastructureParameters_ContainApimServiceNames()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var devJson = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "dev.json"), cancellationToken);
        var prodJson = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "prod.json"), cancellationToken);
        var devBicepParameters = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "dev.bicepparam"), cancellationToken);
        var prodBicepParameters = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "prod.bicepparam"), cancellationToken);

        Assert.Contains("apim-hsl-bike-data-aggregator-dev", devJson, StringComparison.Ordinal);
        Assert.Contains("apim-hsl-bike-data-aggregator-prod", prodJson, StringComparison.Ordinal);
        Assert.Contains("apim-hsl-bike-data-aggregator-dev", devBicepParameters, StringComparison.Ordinal);
        Assert.Contains("apim-hsl-bike-data-aggregator-prod", prodBicepParameters, StringComparison.Ordinal);
    }

    private static string GetRepositoryFilePath(params string[] parts)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(new[] { repositoryRoot }.Concat(parts).ToArray());
    }
}
