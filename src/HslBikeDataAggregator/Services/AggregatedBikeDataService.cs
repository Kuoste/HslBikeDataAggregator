using HslBikeDataAggregator.Storage;

using HslBikeDataAggregator.Models;

namespace HslBikeDataAggregator.Services;

public sealed class AggregatedBikeDataService(IBikeDataBlobStorage bikeDataBlobStorage)
{
    public Task<IReadOnlyList<BikeStation>> GetStationsAsync(CancellationToken cancellationToken)
        => bikeDataBlobStorage.GetLatestStationsAsync(cancellationToken);

    public Task<IReadOnlyList<StationSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken)
        => bikeDataBlobStorage.GetRecentSnapshotsAsync(cancellationToken);

    public Task<IReadOnlyList<HourlyAvailability>> GetAvailabilityAsync(string stationId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);

        return bikeDataBlobStorage.GetAvailabilityProfileAsync(stationId, cancellationToken);
    }

    public Task<IReadOnlyList<StationHistory>> GetDestinationsAsync(string stationId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);

        return bikeDataBlobStorage.GetStationDestinationsAsync(stationId, cancellationToken);
    }
}
