using System.Text.Json.Serialization;

namespace HslBikeDataAggregator.Models;

public sealed record MonthlyStationStatistics
{
    public static MonthlyStationStatistics Empty { get; } = new()
    {
        Month = string.Empty,
        Demand = DemandProfile.Empty,
        Destinations = ColumnarTable.Empty
    };

    [JsonPropertyName("month")]
    public required string Month { get; init; }

    [JsonPropertyName("demand")]
    public required DemandProfile Demand { get; init; }

    [JsonPropertyName("destinations")]
    public required ColumnarTable Destinations { get; init; }
}
