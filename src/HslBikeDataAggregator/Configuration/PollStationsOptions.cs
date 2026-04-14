namespace HslBikeDataAggregator.Configuration;

public sealed class PollStationsOptions
{
    public string DigitransitSubscriptionKey { get; set; } = string.Empty;

    public int SnapshotHistoryLimit { get; set; } = 60;
}
