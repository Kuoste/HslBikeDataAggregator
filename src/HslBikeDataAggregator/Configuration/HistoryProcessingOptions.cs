namespace HslBikeDataAggregator.Configuration;

public sealed class HistoryProcessingOptions
{
    public const string DefaultTripHistoryUrlPattern = "https://dev.hsl.fi/citybikes/od-trips-{0:yyyy}/{0:yyyy-MM}.csv";
    public const int DefaultRollingWindowMonthCount = 2;
    public const int DefaultAvailabilityProbeMonthCount = 12;

    public string TripHistoryUrlPattern { get; set; } = DefaultTripHistoryUrlPattern;

    public int RollingWindowMonthCount { get; set; } = DefaultRollingWindowMonthCount;

    public int AvailabilityProbeMonthCount { get; set; } = DefaultAvailabilityProbeMonthCount;
}
