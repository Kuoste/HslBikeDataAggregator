namespace HslBikeDataAggregator.Configuration;

public sealed class PollStationsOptions
{
    public string DigitransitSubscriptionKey { get; set; } = string.Empty;

    public int SnapshotHistoryLimit { get; set; } = 60;

    /// <summary>
    /// Number of times to retry fetching stations when the API returns an empty list
    /// (e.g. during cold start). Set to 0 to disable application-level retries.
    /// </summary>
    public int EmptyResponseRetryCount { get; set; } = 3;

    /// <summary>
    /// Delay in seconds between retries when the API returns an empty station list.
    /// </summary>
    public int EmptyResponseRetryDelaySeconds { get; set; } = 60;
}
