using HslBikeDataAggregator.Storage;

using HslBikeDataAggregator.Models;

namespace HslBikeDataAggregator.Services;

public sealed class AggregatedBikeDataService(
    IBikeDataBlobStorage bikeDataBlobStorage,
    LiveStationCacheService liveStationCacheService)
{
    public Task<IReadOnlyList<BikeStation>> GetStationsAsync(CancellationToken cancellationToken)
        => liveStationCacheService.GetStationsAsync(cancellationToken);

    public async Task<SnapshotTimeSeries> GetSnapshotsAsync(CancellationToken cancellationToken)
        => await bikeDataBlobStorage.GetSnapshotTimeSeriesAsync(cancellationToken) ?? SnapshotTimeSeries.Empty;

    public async Task<MonthlyStationStatistics> GetStatisticsAsync(string stationId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);

        return await bikeDataBlobStorage.GetMonthlyStatisticsAsync(stationId, cancellationToken) ?? MonthlyStationStatistics.Empty;
    }
}
