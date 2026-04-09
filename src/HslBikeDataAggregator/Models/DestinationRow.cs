namespace HslBikeDataAggregator.Models;

public sealed record DestinationRow
{
    public required string ArrivalStationId { get; init; }

    public required int TripCount { get; init; }

    public required int AverageDurationSeconds { get; init; }

    public required int AverageDistanceMetres { get; init; }
}
