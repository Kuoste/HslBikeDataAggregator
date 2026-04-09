using HslBikeDataAggregator.Models;

namespace HslBikeDataAggregator.Storage;

public interface IBikeDataBlobStorage
{
    Task<SnapshotTimeSeries?> GetSnapshotTimeSeriesAsync(CancellationToken cancellationToken);

    Task<MonthlyStationStatistics?> GetMonthlyStatisticsAsync(string stationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListMonthlyStatisticStationIdsAsync(CancellationToken cancellationToken);

    Task WriteSnapshotTimeSeriesAsync(SnapshotTimeSeries snapshotTimeSeries, CancellationToken cancellationToken);

    Task WriteMonthlyStatisticsAsync(string stationId, MonthlyStationStatistics monthlyStatistics, CancellationToken cancellationToken);

    Task DeleteMonthlyStatisticsAsync(string stationId, CancellationToken cancellationToken);
}
