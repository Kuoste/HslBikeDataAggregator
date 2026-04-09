using System.Text.Json.Serialization;

namespace HslBikeDataAggregator.Models;

public sealed record DemandProfile
{
    public static DemandProfile Empty { get; } = new()
    {
        DeparturesByHour = CreateBuckets(),
        ArrivalsByHour = CreateBuckets(),
        WeekdayDeparturesByHour = CreateBuckets(),
        WeekendDeparturesByHour = CreateBuckets(),
        WeekdayArrivalsByHour = CreateBuckets(),
        WeekendArrivalsByHour = CreateBuckets()
    };

    [JsonPropertyName("departuresByHour")]
    public required int[] DeparturesByHour { get; init; }

    [JsonPropertyName("arrivalsByHour")]
    public required int[] ArrivalsByHour { get; init; }

    [JsonPropertyName("weekdayDeparturesByHour")]
    public required int[] WeekdayDeparturesByHour { get; init; }

    [JsonPropertyName("weekendDeparturesByHour")]
    public required int[] WeekendDeparturesByHour { get; init; }

    [JsonPropertyName("weekdayArrivalsByHour")]
    public required int[] WeekdayArrivalsByHour { get; init; }

    [JsonPropertyName("weekendArrivalsByHour")]
    public required int[] WeekendArrivalsByHour { get; init; }

    private static int[] CreateBuckets() => new int[24];
}
