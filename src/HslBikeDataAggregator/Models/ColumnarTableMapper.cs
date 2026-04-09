namespace HslBikeDataAggregator.Models;

public static class ColumnarTableMapper
{
    private static readonly string[] DestinationFields =
    [
        "arrivalStationId",
        "tripCount",
        "averageDurationSeconds",
        "averageDistanceMetres"
    ];

    public static ColumnarTable ToDestinationTable(IReadOnlyList<DestinationRow> destinations)
    {
        ArgumentNullException.ThrowIfNull(destinations);

        return new ColumnarTable
        {
            Fields = [.. DestinationFields],
            Rows = destinations
                .Select(static destination => new object[]
                {
                    destination.ArrivalStationId,
                    destination.TripCount,
                    destination.AverageDurationSeconds,
                    destination.AverageDistanceMetres
                })
                .ToArray()
        };
    }
}
