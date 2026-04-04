namespace HslBikeDataAggregator.Services;

public sealed record HistoryProcessingResult
{
    public required int SourceCount { get; init; }

    public required int JourneyCount { get; init; }

    public required int StationCount { get; init; }
}
