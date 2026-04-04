using HslBikeDataAggregator.Models;

namespace HslBikeDataAggregator.Services;

public sealed class AvailabilityProfileService
{
    /// <summary>
    /// Builds per-station hourly bike availability averages from stored snapshots.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<HourlyAvailability>> BuildProfiles(IReadOnlyList<StationSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        var profiles = snapshots
            .SelectMany(snapshot => snapshot.BikeCounts.Select(bikeCount => new SnapshotObservation(
                bikeCount.Key,
                snapshot.Timestamp.Hour,
                bikeCount.Value)))
            .GroupBy(static observation => observation.StationId, StringComparer.Ordinal)
            .ToDictionary(
                stationGroup => stationGroup.Key,
                stationGroup => (IReadOnlyList<HourlyAvailability>)stationGroup
                    .GroupBy(static observation => observation.Hour)
                    .OrderBy(static hourGroup => hourGroup.Key)
                    .Select(hourGroup => new HourlyAvailability
                    {
                        Hour = hourGroup.Key,
                        AverageBikesAvailable = hourGroup.Average(static observation => observation.BikesAvailable)
                    })
                    .ToArray(),
                StringComparer.Ordinal);

        return profiles;
    }

    private readonly record struct SnapshotObservation(string StationId, int Hour, int BikesAvailable);
}
