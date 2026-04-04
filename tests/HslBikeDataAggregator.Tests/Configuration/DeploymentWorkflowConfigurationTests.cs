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
        Assert.Contains("func-hsl-bike-data-aggregator-dev", devYaml, StringComparison.Ordinal);
        Assert.Contains("rg-hsl-bike-data-aggregator-prod", prodYaml, StringComparison.Ordinal);
        Assert.Contains("func-hsl-bike-data-aggregator-prod", prodYaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BicepParameterFiles_ContainRecommendedFunctionAppNames()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var devParameters = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "dev.bicepparam"), cancellationToken);
        var prodParameters = await File.ReadAllTextAsync(GetRepositoryFilePath("infra", "prod.bicepparam"), cancellationToken);

        Assert.Contains("func-hsl-bike-data-aggregator-dev", devParameters, StringComparison.Ordinal);
        Assert.Contains("func-hsl-bike-data-aggregator-prod", prodParameters, StringComparison.Ordinal);
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
