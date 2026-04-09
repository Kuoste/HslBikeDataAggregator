using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HslBikeDataAggregator.Models;

namespace HslBikeDataAggregator.Storage;

public sealed class BikeDataBlobStorage(BlobContainerClient blobContainerClient) : IBikeDataBlobStorage
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public Task<SnapshotTimeSeries?> GetSnapshotTimeSeriesAsync(CancellationToken cancellationToken)
        => ReadBlobAsync<SnapshotTimeSeries>(BikeDataBlobNames.RecentSnapshots, cancellationToken);

    public Task<MonthlyStationStatistics?> GetMonthlyStatisticsAsync(string stationId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);

        return ReadBlobAsync<MonthlyStationStatistics>(BikeDataBlobNames.MonthlyStatistics(stationId), cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListMonthlyStatisticStationIdsAsync(CancellationToken cancellationToken)
    {
        await blobContainerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        return await ListStationIdsByPrefixAsync(BikeDataBlobNames.MonthlyStatisticsPrefix, cancellationToken);
    }

    public async Task WriteSnapshotTimeSeriesAsync(SnapshotTimeSeries snapshotTimeSeries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshotTimeSeries);

        await blobContainerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await blobContainerClient
            .GetBlobClient(BikeDataBlobNames.RecentSnapshots)
            .UploadAsync(BinaryData.FromObjectAsJson(snapshotTimeSeries, SerializerOptions), overwrite: true, cancellationToken);
    }

    public async Task WriteMonthlyStatisticsAsync(string stationId, MonthlyStationStatistics monthlyStatistics, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);
        ArgumentNullException.ThrowIfNull(monthlyStatistics);

        await blobContainerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await blobContainerClient
            .GetBlobClient(BikeDataBlobNames.MonthlyStatistics(stationId))
            .UploadAsync(BinaryData.FromObjectAsJson(monthlyStatistics, SerializerOptions), overwrite: true, cancellationToken);
    }

    public async Task DeleteMonthlyStatisticsAsync(string stationId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);

        await blobContainerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await blobContainerClient
            .GetBlobClient(BikeDataBlobNames.MonthlyStatistics(stationId))
            .DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    private async Task<T?> ReadBlobAsync<T>(string blobName, CancellationToken cancellationToken)
        where T : class
    {
        var content = await DownloadBlobContentAsync(blobName, cancellationToken);
        return content?.ToObjectFromJson<T>(SerializerOptions);
    }

    private async Task<BinaryData?> DownloadBlobContentAsync(string blobName, CancellationToken cancellationToken)
    {
        await blobContainerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        try
        {
            var response = await blobContainerClient
                .GetBlobClient(blobName)
                .DownloadContentAsync(cancellationToken);

            return response.Value.Content;
        }
        catch (Azure.RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<string>> ListStationIdsByPrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        var stationIds = new List<string>();
        await foreach (var blobItem in blobContainerClient.GetBlobsAsync(traits: BlobTraits.None, states: BlobStates.None, prefix: prefix, cancellationToken: cancellationToken))
        {
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
}
