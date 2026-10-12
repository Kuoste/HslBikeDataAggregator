using Azure.Core;
using Azure.Identity;

namespace HslBikeDataAggregator.Storage;

/// <summary>
/// Chooses the credential used for identity-based blob storage access.
/// </summary>
public static class StorageCredentialFactory
{
    /// <summary>
    /// With a user-assigned managed identity client ID, uses <see cref="ManagedIdentityCredential"/> directly:
    /// <see cref="DefaultAzureCredential"/> would first probe environment, workload identity and other sources,
    /// adding latency to the first blob read on every cold-started instance. Without a client ID (for example
    /// local development against a real storage account) it falls back to <see cref="DefaultAzureCredential"/>.
    /// </summary>
    public static TokenCredential Create(string? managedIdentityClientId) =>
        string.IsNullOrWhiteSpace(managedIdentityClientId)
            ? new DefaultAzureCredential()
            : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(managedIdentityClientId));
}
