using System.Text.Json;

using Azure;
using Azure.Storage.Blobs;

using HslBikeDataAggregator.Models;

namespace HslBikeDataAggregator.Storage;

public sealed class BikeDataBlobStorage(BlobContainerClient blobContainerClient) : IBikeDataBlobStorage
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<BikeStation>> GetLatestStationsAsync(CancellationToken cancellationToken)
        => ReadBlobAsync<BikeStation>(BikeDataBlobNames.LatestStations, cancellationToken);

    public async Task<IReadOnlyList<StationSnapshot>> GetRecentSnapshotsAsync(CancellationToken cancellationToken)
        => await ReadBlobAsync<StationSnapshot>(BikeDataBlobNames.RecentSnapshots, cancellationToken);

    public Task<IReadOnlyList<StationHistory>> GetStationDestinationsAsync(string stationId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);

        return ReadBlobAsync<StationHistory>(BikeDataBlobNames.DestinationProfile(stationId), cancellationToken);
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

    public async Task WriteLatestStationsAsync(IReadOnlyList<BikeStation> stations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stations);

        await blobContainerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await blobContainerClient
            .GetBlobClient(BikeDataBlobNames.LatestStations)
            .UploadAsync(BinaryData.FromObjectAsJson(stations, SerializerOptions), overwrite: true, cancellationToken);
    }

    public async Task WriteRecentSnapshotsAsync(IReadOnlyList<StationSnapshot> snapshots, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        await blobContainerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await blobContainerClient
            .GetBlobClient(BikeDataBlobNames.RecentSnapshots)
            .UploadAsync(BinaryData.FromObjectAsJson(snapshots, SerializerOptions), overwrite: true, cancellationToken);
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
}
