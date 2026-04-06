using System.Text.RegularExpressions;

namespace HslBikeDataAggregator.Storage;

public static partial class BikeDataBlobNames
{
    public const string ContainerName = "bike-data";
    public const string RecentSnapshots = "snapshots/recent.json";

    /// <summary>
    /// Returns the blob name for a station's hourly availability profile.
    /// </summary>
    public static string AvailabilityProfile(string stationId) => $"availability/{SanitiseStationId(stationId)}.json";

    /// <summary>
    /// Returns the blob name for a station's destination profile.
    /// </summary>
    public static string DestinationProfile(string stationId) => $"destinations/{SanitiseStationId(stationId)}.json";

    /// <summary>
    /// Validates that a station ID contains only safe characters (alphanumeric, hyphens, underscores)
    /// to prevent path-traversal attacks in blob name construction.
    /// </summary>
    internal static string SanitiseStationId(string stationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);

        if (!SafeStationIdPattern().IsMatch(stationId))
        {
            throw new ArgumentException("Station ID contains invalid characters.", nameof(stationId));
        }

        return stationId;
    }

    [GeneratedRegex(@"^[\w\-]+$")]
    private static partial Regex SafeStationIdPattern();
}
