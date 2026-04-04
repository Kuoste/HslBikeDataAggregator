using HslBikeDataAggregator.Models;

namespace HslBikeDataAggregator.Storage;

public interface IBikeDataBlobStorage
{
    Task<IReadOnlyList<BikeStation>> GetLatestStationsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<StationSnapshot>> GetRecentSnapshotsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<StationHistory>> GetStationDestinationsAsync(string stationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListStationDestinationIdsAsync(CancellationToken cancellationToken);

    Task WriteLatestStationsAsync(IReadOnlyList<BikeStation> stations, CancellationToken cancellationToken);

    Task WriteRecentSnapshotsAsync(IReadOnlyList<StationSnapshot> snapshots, CancellationToken cancellationToken);

    Task WriteStationDestinationsAsync(string stationId, IReadOnlyList<StationHistory> destinations, CancellationToken cancellationToken);

    Task DeleteStationDestinationsAsync(string stationId, CancellationToken cancellationToken);
}
