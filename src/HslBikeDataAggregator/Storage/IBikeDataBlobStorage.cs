using HslBikeDataAggregator.Models;

namespace HslBikeDataAggregator.Storage;

public interface IBikeDataBlobStorage
{
    Task<IReadOnlyList<StationSnapshot>> GetRecentSnapshotsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<HourlyAvailability>> GetAvailabilityProfileAsync(string stationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StationHistory>> GetStationDestinationsAsync(string stationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListStationDestinationIdsAsync(CancellationToken cancellationToken);

    Task WriteRecentSnapshotsAsync(IReadOnlyList<StationSnapshot> snapshots, CancellationToken cancellationToken);

    Task WriteAvailabilityProfileAsync(string stationId, IReadOnlyList<HourlyAvailability> availabilityProfile, CancellationToken cancellationToken);

    Task WriteStationDestinationsAsync(string stationId, IReadOnlyList<StationHistory> destinations, CancellationToken cancellationToken);

    Task DeleteStationDestinationsAsync(string stationId, CancellationToken cancellationToken);
}
