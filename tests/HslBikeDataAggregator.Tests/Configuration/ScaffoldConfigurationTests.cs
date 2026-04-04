using System.Text.Json;

namespace HslBikeDataAggregator.Tests.Configuration;

public sealed class ScaffoldConfigurationTests
{
    [Fact]
    public async Task LocalSettingsTemplate_ContainsDigitransitSubscriptionKeyPlaceholder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var json = await File.ReadAllTextAsync(GetRepositoryFilePath("src", "HslBikeDataAggregator", "local.settings.example.json"), cancellationToken);
        using var document = JsonDocument.Parse(json);

        var key = document.RootElement
            .GetProperty("Values")
            .GetProperty("DigitransitSubscriptionKey")
            .GetString();

        Assert.Equal(string.Empty, key);
    }

    [Fact]
    public async Task LocalSettingsTemplate_ContainsGitHubPagesCorsOrigin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var json = await File.ReadAllTextAsync(GetRepositoryFilePath("src", "HslBikeDataAggregator", "local.settings.example.json"), cancellationToken);
        using var document = JsonDocument.Parse(json);

        var corsOrigin = document.RootElement
            .GetProperty("Host")
            .GetProperty("CORS")
            .GetString();

        Assert.Equal("https://kuoste.github.io", corsOrigin);
    }

    private static string GetRepositoryFilePath(params string[] parts)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(new[] { repositoryRoot }.Concat(parts).ToArray());
    }
}
