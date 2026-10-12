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
    public async Task GlobalJson_OptsIntoMicrosoftTestingPlatformRunner()
    {
        // xunit.v3 4.x runs on Microsoft.Testing.Platform, which the .NET 10 SDK only supports
        // through dotnet test when the repository opts in via global.json.
        var cancellationToken = TestContext.Current.CancellationToken;
        var json = await File.ReadAllTextAsync(GetRepositoryFilePath("global.json"), cancellationToken);
        using var document = JsonDocument.Parse(json);

        var runner = document.RootElement
            .GetProperty("test")
            .GetProperty("runner")
            .GetString();

        Assert.Equal("Microsoft.Testing.Platform", runner);
    }

    [Fact]
    public async Task LocalSettingsTemplate_DoesNotContainHostSection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var json = await File.ReadAllTextAsync(GetRepositoryFilePath("src", "HslBikeDataAggregator", "local.settings.example.json"), cancellationToken);
        using var document = JsonDocument.Parse(json);

        Assert.False(document.RootElement.TryGetProperty("Host", out _));
    }

    [Fact]
    public async Task LocalSettingsTemplate_ContainsReducedPollIntervalDefault()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var json = await File.ReadAllTextAsync(GetRepositoryFilePath("src", "HslBikeDataAggregator", "local.settings.example.json"), cancellationToken);
        using var document = JsonDocument.Parse(json);

        var pollIntervalCron = document.RootElement
            .GetProperty("Values")
            .GetProperty("PollIntervalCron")
            .GetString();

        Assert.Equal("0 */15 * * * *", pollIntervalCron);
    }

    private static string GetRepositoryFilePath(params string[] parts)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(new[] { repositoryRoot }.Concat(parts).ToArray());
    }
}
