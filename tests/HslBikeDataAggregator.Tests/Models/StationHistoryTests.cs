using System.Text.Json;

using HslBikeDataAggregator.Models;

namespace HslBikeDataAggregator.Tests.Models;

public sealed class StationHistoryTests
{
    [Fact]
    public void Serialise_UsesBritishEnglishDistancePropertyName()
    {
        var history = new StationHistory
        {
            DepartureStationId = "001",
            ArrivalStationId = "002",
            TripCount = 12,
            AverageDurationSeconds = 321.5,
            AverageDistanceMetres = 654.25,
        };

        var json = JsonSerializer.Serialize(history);

        Assert.Contains("\"averageDistanceMetres\"", json);
        Assert.DoesNotContain("averageDistanceMeters", json);
    }

    [Fact]
    public void Deserialise_ReadsBritishEnglishDistancePropertyName()
    {
        const string json = """
            {
              "departureStationId": "001",
              "arrivalStationId": "002",
              "tripCount": 12,
              "averageDurationSeconds": 321.5,
              "averageDistanceMetres": 654.25
            }
            """;

        var history = JsonSerializer.Deserialize<StationHistory>(json);

        Assert.NotNull(history);
        Assert.Equal(654.25, history.AverageDistanceMetres);
    }
}
