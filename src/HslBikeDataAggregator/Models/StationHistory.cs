using System.Text.Json.Serialization;

namespace HslBikeDataAggregator.Models;

public sealed record StationHistory
{
    [JsonPropertyName("departureStationId")]
    public required string DepartureStationId { get; init; }

    [JsonPropertyName("arrivalStationId")]
    public required string ArrivalStationId { get; init; }

    [JsonPropertyName("tripCount")]
    public required int TripCount { get; init; }

    [JsonPropertyName("averageDurationSeconds")]
    public required double AverageDurationSeconds { get; init; }

    [JsonPropertyName("averageDistanceMetres")]
    public required double AverageDistanceMetres { get; init; }
}
