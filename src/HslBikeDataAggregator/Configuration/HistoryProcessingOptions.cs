namespace HslBikeDataAggregator.Configuration;

public sealed class HistoryProcessingOptions
{
    public const string DefaultTripHistoryUrlPattern = "https://dev.hsl.fi/citybikes/od-trips-{0:yyyy}/{0:yyyy-MM}.csv";
    public const string DefaultAllowedTripHistoryHost = "dev.hsl.fi";
    public const int DefaultRollingWindowMonthCount = 2;
    public const int DefaultAvailabilityProbeMonthCount = 12;

    public string TripHistoryUrlPattern { get; set; } = DefaultTripHistoryUrlPattern;

    /// <summary>
    /// The only hostname allowed in resolved trip history URLs.
    /// Prevents SSRF if the URL pattern is overridden via app settings.
    /// </summary>
    public string AllowedTripHistoryHost { get; set; } = DefaultAllowedTripHistoryHost;

    public int RollingWindowMonthCount { get; set; } = DefaultRollingWindowMonthCount;

    public int AvailabilityProbeMonthCount { get; set; } = DefaultAvailabilityProbeMonthCount;
}
