using HslBikeDataAggregator.Configuration;
using HslBikeDataAggregator.Models;
using HslBikeDataAggregator.Storage;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HslBikeDataAggregator.Services;

public sealed class PollStationsService(
    DigitransitStationClient digitransitStationClient,
    IBikeDataBlobStorage bikeDataBlobStorage,
    AvailabilityProfileService availabilityProfileService,
    IOptions<PollStationsOptions> options,
    TimeProvider timeProvider,
    ILogger<PollStationsService> logger) : IPollStationsService
{
    public async Task<PollStationsResult> PollAsync(CancellationToken cancellationToken)
    {
        var stations = await digitransitStationClient.FetchStationsAsync(cancellationToken);
        var timestamp = timeProvider.GetUtcNow();
        var snapshotHistoryLimit = Math.Max(1, options.Value.SnapshotHistoryLimit);
        var existingSnapshots = await bikeDataBlobStorage.GetRecentSnapshotsAsync(cancellationToken);
        var currentSnapshot = new StationSnapshot
        {
            Timestamp = timestamp,
            BikeCounts = stations.ToDictionary(station => station.Id, station => station.BikesAvailable, StringComparer.Ordinal)
        };

        var updatedSnapshots = existingSnapshots
            .Append(currentSnapshot)
            .OrderBy(snapshot => snapshot.Timestamp)
            .TakeLast(snapshotHistoryLimit)
            .ToArray();
        var availabilityProfiles = availabilityProfileService.BuildProfiles(updatedSnapshots);

        await bikeDataBlobStorage.WriteRecentSnapshotsAsync(updatedSnapshots, cancellationToken);
        foreach (var availabilityProfile in availabilityProfiles)
        {
            await bikeDataBlobStorage.WriteAvailabilityProfileAsync(availabilityProfile.Key, availabilityProfile.Value, cancellationToken);
        }

        logger.LogInformation(
            "Processed {StationCount} stations and stored {SnapshotCount} snapshots plus {AvailabilityProfileCount} hourly availability profiles at {Timestamp}.",
            stations.Count,
            updatedSnapshots.Length,
            availabilityProfiles.Count,
            timestamp);

        return new PollStationsResult(timestamp, stations.Count, updatedSnapshots.Length);
    }
}
