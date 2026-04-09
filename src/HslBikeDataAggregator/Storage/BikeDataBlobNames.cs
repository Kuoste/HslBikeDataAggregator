namespace HslBikeDataAggregator.Storage;

public static class BikeDataBlobNames
{
    public const string ContainerName = "bike-data";
    public const string RecentSnapshots = "snapshots/recent.json";
    public const string MonthlyStatisticsPrefix = "monthly-stats/";

    /// <summary>
    /// Returns the blob name for a station's monthly statistics profile.
    /// </summary>
    public static string MonthlyStatistics(string stationId) => $"{MonthlyStatisticsPrefix}{SanitiseStationId(stationId)}.json";

    /// <summary>
    /// Validates that a station ID does not contain path-traversal or control characters.
    /// Allows printable characters except forward/back slashes to prevent directory traversal.
    /// </summary>
    internal static string SanitiseStationId(string stationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);

        // Block path separators to prevent directory traversal
        if (stationId.Contains('/') || stationId.Contains('\\'))
        {
            throw new ArgumentException("Station ID cannot contain path separators.", nameof(stationId));
        }

        // Block control characters (including null bytes) to prevent injection attacks
        if (stationId.Any(char.IsControl))
        {
            throw new ArgumentException("Station ID cannot contain control characters.", nameof(stationId));
        }

        // Block parent directory traversal attempts
        if (stationId.Contains(".."))
        {
            throw new ArgumentException("Station ID cannot contain '..'.", nameof(stationId));
        }

        return stationId;
    }
}
