namespace HslBikeDataAggregator.Tests.Configuration;

/// <summary>
/// Guards documentation against the former <c>TmiKuoste</c> repository owner; the canonical owner is <c>Kuoste</c>.
/// </summary>
public sealed class RepositoryAddressTests
{
    [Theory]
    [InlineData("README.md")]
    [InlineData("CLAUDE.md")]
    [InlineData(".github", "copilot-instructions.md")]
    [InlineData(".github", "instructions", "azure-functions.instructions.md")]
    public async Task Documentation_DoesNotReferenceFormerRepositoryOwner(params string[] pathParts)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var content = await File.ReadAllTextAsync(GetRepositoryFilePath(pathParts), cancellationToken);

        Assert.DoesNotContain("tmikuoste", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Readme_LinksFrontendRepositoryUnderCanonicalOwner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var content = await File.ReadAllTextAsync(GetRepositoryFilePath("README.md"), cancellationToken);

        Assert.Contains("https://github.com/Kuoste/HslBikeApp", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FunctionsInstructions_NameLiveFrontendOrigin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var content = await File.ReadAllTextAsync(
            GetRepositoryFilePath(".github", "instructions", "azure-functions.instructions.md"), cancellationToken);

        Assert.Contains("https://kuoste.github.io", content, StringComparison.Ordinal);
    }

    private static string GetRepositoryFilePath(params string[] parts)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(new[] { repositoryRoot }.Concat(parts).ToArray());
    }
}
