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
        Assert.Contains("@infra/dev.json", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeployProdWorkflow_TargetsProdEnvironment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var yaml = await File.ReadAllTextAsync(GetRepositoryFilePath(".github", "workflows", "deploy-prod.yml"), cancellationToken);

        Assert.Contains("environment: prod", yaml, StringComparison.Ordinal);
        Assert.Contains("@infra/prod.json", yaml, StringComparison.Ordinal);
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
        var devJson = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "dev.json"), cancellationToken);
        var prodJson = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "prod.json"), cancellationToken);
        var devBicepParameters = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "dev.bicepparam"), cancellationToken);
        var prodBicepParameters = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "prod.bicepparam"), cancellationToken);

        Assert.Contains("param historyProcessingCron string", mainBicep, StringComparison.Ordinal);
        Assert.Contains("name: 'HistoryProcessingCron'", mainBicep, StringComparison.Ordinal);
        Assert.Contains("\"historyProcessingCron\"", devJson, StringComparison.Ordinal);
        Assert.Contains("\"historyProcessingCron\"", prodJson, StringComparison.Ordinal);
        Assert.Contains("param historyProcessingCron = '0 0 2 * * *'", devBicepParameters, StringComparison.Ordinal);
        Assert.Contains("param historyProcessingCron = '0 0 2 * * *'", prodBicepParameters, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InfrastructureParameters_ContainReducedPollIntervalDefaults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mainBicep = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "main.bicep"), cancellationToken);
        var devJson = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "dev.json"), cancellationToken);
        var prodJson = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "prod.json"), cancellationToken);
        var devBicepParameters = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "dev.bicepparam"), cancellationToken);
        var prodBicepParameters = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "prod.bicepparam"), cancellationToken);

        Assert.Contains("param pollIntervalCron string = '0 */15 * * * *'", mainBicep, StringComparison.Ordinal);
        Assert.Contains("\"pollIntervalCron\"", devJson, StringComparison.Ordinal);
        Assert.Contains("\"pollIntervalCron\"", prodJson, StringComparison.Ordinal);
        Assert.Contains("\"0 */15 * * * *\"", devJson, StringComparison.Ordinal);
        Assert.Contains("\"0 */15 * * * *\"", prodJson, StringComparison.Ordinal);
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
        var devJson = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "dev.json"), cancellationToken);
        var prodJson = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "prod.json"), cancellationToken);
        var devBicepParameters = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "dev.bicepparam"), cancellationToken);
        var prodBicepParameters = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "prod.bicepparam"), cancellationToken);

        Assert.Contains("param corsAllowedOrigins array", mainBicep, StringComparison.Ordinal);
        Assert.Contains("allowedOrigins: corsAllowedOrigins", mainBicep, StringComparison.Ordinal);

        Assert.Contains("\"corsAllowedOrigins\"", devJson, StringComparison.Ordinal);
        Assert.Contains("http://localhost:5000", devJson, StringComparison.Ordinal);
        Assert.Contains("\"corsAllowedOrigins\"", prodJson, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", prodJson, StringComparison.Ordinal);

        Assert.Contains("param corsAllowedOrigins", devBicepParameters, StringComparison.Ordinal);
        Assert.Contains("http://localhost:5000", devBicepParameters, StringComparison.Ordinal);
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

    private static string GetRepositoryFilePath(params string[] parts)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(new[] { repositoryRoot }.Concat(parts).ToArray());
    }
}
