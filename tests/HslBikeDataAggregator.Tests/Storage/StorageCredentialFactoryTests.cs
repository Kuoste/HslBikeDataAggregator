using Azure.Identity;

using HslBikeDataAggregator.Storage;

namespace HslBikeDataAggregator.Tests.Storage;

public sealed class StorageCredentialFactoryTests
{
    [Fact]
    public void Create_WithClientId_UsesManagedIdentityCredentialDirectly()
    {
        var credential = StorageCredentialFactory.Create("00000000-0000-0000-0000-000000000001");

        Assert.IsType<ManagedIdentityCredential>(credential);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutClientId_FallsBackToDefaultAzureCredential(string? clientId)
    {
        var credential = StorageCredentialFactory.Create(clientId);

        Assert.IsType<DefaultAzureCredential>(credential);
    }
}
