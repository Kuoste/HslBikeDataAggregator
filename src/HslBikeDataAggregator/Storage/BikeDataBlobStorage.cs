using System.Text.Json;

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HslBikeDataAggregator.Models;

namespace HslBikeDataAggregator.Storage;

public sealed class BikeDataBlobStorage(BlobContainerClient blobContainerClient) : IBikeDataBlobStorage
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<StationSnapshot>> GetRecentSnapshotsAsync(CancellationToken cancellationToken)
        => await ReadBlobAsync<StationSnapshot>(BikeDataBlobNames.RecentSnapshots, cancellationToken);

    public Task<IReadOnlyList<HourlyAvailability>> GetAvailabilityProfileAsync(string stationId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);

        return ReadBlobAsync<HourlyAvailability>(BikeDataBlobNames.AvailabilityProfile(stationId), cancellationToken);
    }

    public Task<IReadOnlyList<StationHistory>> GetStationDestinationsAsync(string stationId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);

        return ReadBlobAsync<StationHistory>(BikeDataBlobNames.DestinationProfile(stationId), cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListStationDestinationIdsAsync(CancellationToken cancellationToken)
    {
        await blobContainerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var stationIds = new List<string>();
        await foreach (var blobItem in blobContainerClient.GetBlobsAsync(traits: BlobTraits.None, states: BlobStates.None, prefix: "destinations/", cancellationToken: cancellationToken))
        {
            const string prefix = "destinations/";
            const string suffix = ".json";
            if (!blobItem.Name.StartsWith(prefix, StringComparison.Ordinal)
                || !blobItem.Name.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            stationIds.Add(blobItem.Name[prefix.Length..^suffix.Length]);
        }

        return stationIds;
    }

    private async Task<IReadOnlyList<T>> ReadBlobAsync<T>(string blobName, CancellationToken cancellationToken)
    {
        await blobContainerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        try
        {
            var response = await blobContainerClient
                .GetBlobClient(blobName)
                .DownloadContentAsync(cancellationToken);

            return response.Value.Content.ToObjectFromJson<List<T>>(SerializerOptions) ?? [];
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return [];
        }
    }

    public async Task WriteRecentSnapshotsAsync(IReadOnlyList<StationSnapshot> snapshots, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        await blobContainerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await blobContainerClient
            .GetBlobClient(BikeDataBlobNames.RecentSnapshots)
            .UploadAsync(BinaryData.FromObjectAsJson(snapshots, SerializerOptions), overwrite: true, cancellationToken);
    }

    public async Task WriteAvailabilityProfileAsync(string stationId, IReadOnlyList<HourlyAvailability> availabilityProfile, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);
        ArgumentNullException.ThrowIfNull(availabilityProfile);

        await blobContainerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await blobContainerClient
            .GetBlobClient(BikeDataBlobNames.AvailabilityProfile(stationId))
            .UploadAsync(BinaryData.FromObjectAsJson(availabilityProfile, SerializerOptions), overwrite: true, cancellationToken);
    }

    public async Task WriteStationDestinationsAsync(string stationId, IReadOnlyList<StationHistory> destinations, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);
        ArgumentNullException.ThrowIfNull(destinations);

        await blobContainerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await blobContainerClient
            .GetBlobClient(BikeDataBlobNames.DestinationProfile(stationId))
            .UploadAsync(BinaryData.FromObjectAsJson(destinations, SerializerOptions), overwrite: true, cancellationToken);
    }

    public async Task DeleteStationDestinationsAsync(string stationId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);

        await blobContainerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await blobContainerClient
            .GetBlobClient(BikeDataBlobNames.DestinationProfile(stationId))
            .DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }
}
