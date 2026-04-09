namespace HslBikeDataAggregator.Models;

public sealed record StationCountSeries
{
    public required string StationId { get; init; }

    public required IReadOnlyList<int> Counts { get; init; }
}
