using System.Net;
using System.Text;

using HslBikeDataAggregator.Configuration;
using HslBikeDataAggregator.Models;
using HslBikeDataAggregator.Services;
using HslBikeDataAggregator.Storage;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

namespace HslBikeDataAggregator.Tests.Services;

public sealed class AggregatedBikeDataServiceTests
{
    private static readonly CancellationToken CancellationToken = CancellationToken.None;

    [Fact]
    public async Task GetStationsAsync_ReturnsStationsFromLiveStationCache()
    {
        var blobStorage = new Mock<IBikeDataBlobStorage>();

        var service = CreateService(
            blobStorage,
            """
            {
              "data": {
                "vehicleRentalStations": [
                  {
                    "stationId": "smoove:001",
                    "name": "Central Station",
                    "lat": 60.1708,
                    "lon": 24.941,
                    "allowPickup": true,
                    "allowDropoff": true,
                    "capacity": 24,
                    "availableVehicles": {
                      "byType": [
                        { "count": 8 }
                      ]
                    },
                    "availableSpaces": {
                      "byType": [
                        { "count": 16 }
                      ]
                    }
                  }
                ]
              }
            }
            """);

        var result = await service.GetStationsAsync(CancellationToken);

        var station = Assert.Single(result);
        Assert.Equal("smoove:001", station.Id);
    }

    [Fact]
    public async Task GetStationsAsync_ReturnsEmptyCollectionWhenLiveStationCacheHasNoStations()
    {
        var blobStorage = new Mock<IBikeDataBlobStorage>();

        var service = CreateService(blobStorage, EmptyStationsResponse);

        var result = await service.GetStationsAsync(CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSnapshotsAsync_ReturnsSnapshotsFromBlobStorage()
    {
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetSnapshotTimeSeriesAsync(CancellationToken))
            .ReturnsAsync(new SnapshotTimeSeries
            {
                IntervalMinutes = 15,
                Timestamps =
                [
                    new DateTimeOffset(2026, 4, 4, 9, 45, 0, TimeSpan.Zero)
                ],
                Stations =
                [
                    new StationCountSeries
                    {
                        StationId = "smoove:001",
                        Counts = [8]
                    }
                ]
            });

        var service = CreateService(blobStorage, EmptyStationsResponse);

        var result = await service.GetSnapshotsAsync(CancellationToken);

        Assert.Equal(15, result.IntervalMinutes);
        var station = Assert.Single(result.Stations);
        Assert.Equal(8, Assert.Single(station.Counts));
    }

    [Fact]
    public async Task GetSnapshotsAsync_ReturnsEmptyPayloadWhenBlobStorageHasNoSnapshots()
    {
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetSnapshotTimeSeriesAsync(CancellationToken))
            .ReturnsAsync((SnapshotTimeSeries?)null);

        var service = CreateService(blobStorage, EmptyStationsResponse);

        var result = await service.GetSnapshotsAsync(CancellationToken);

        Assert.Empty(result.Timestamps);
        Assert.Empty(result.Stations);
    }

    [Fact]
    public async Task GetStatisticsAsync_ReturnsMonthlyStatisticsFromBlobStorage()
    {
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetMonthlyStatisticsAsync("smoove:001", CancellationToken))
            .ReturnsAsync(new MonthlyStationStatistics
            {
                Month = "2026-04",
                Demand = new DemandProfile
                {
                    DeparturesByHour = [1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
                    ArrivalsByHour = new int[24],
                    WeekdayDeparturesByHour = [1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
                    WeekendDeparturesByHour = new int[24],
                    WeekdayArrivalsByHour = new int[24],
                    WeekendArrivalsByHour = new int[24]
                },
                Destinations = new ColumnarTable
                {
                    Fields = ["arrivalStationId", "tripCount", "averageDurationSeconds", "averageDistanceMetres"],
                    Rows = [
                        ["smoove:002", 12, 426, 1280]
                    ]
                }
            });

        var service = CreateService(blobStorage, EmptyStationsResponse);

        var result = await service.GetStatisticsAsync("smoove:001", CancellationToken);

        Assert.Equal("2026-04", result.Month);
        Assert.Equal(1, result.Demand.DeparturesByHour[0]);
        Assert.Equal("smoove:002", Assert.IsType<string>(result.Destinations.Rows[0][0]));
    }

    [Fact]
    public async Task GetStatisticsAsync_ReturnsEmptyPayloadWhenBlobStorageHasNoStatistics()
    {
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetMonthlyStatisticsAsync("smoove:001", CancellationToken))
            .ReturnsAsync((MonthlyStationStatistics?)null);

        var service = CreateService(blobStorage, EmptyStationsResponse);

        var result = await service.GetStatisticsAsync("smoove:001", CancellationToken);

        Assert.Equal(string.Empty, result.Month);
        Assert.Empty(result.Destinations.Fields);
    }

    private const string EmptyStationsResponse = """
        {
          "data": {
            "vehicleRentalStations": []
          }
        }
        """;

    private static AggregatedBikeDataService CreateService(Mock<IBikeDataBlobStorage> blobStorage, string liveStationsResponse)
        => new(blobStorage.Object, CreateLiveStationCacheService(liveStationsResponse));

    private static LiveStationCacheService CreateLiveStationCacheService(string responseBody)
    {
        var handler = new StubHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        }));

        var client = new DigitransitStationClient(
            new HttpClient(handler),
            Options.Create(new PollStationsOptions
            {
                DigitransitSubscriptionKey = "test-subscription-key"
            }));

        return new LiveStationCacheService(
            client,
            new FixedTimeProvider(new DateTimeOffset(2026, 4, 6, 10, 0, 0, TimeSpan.Zero)),
            NullLogger<LiveStationCacheService>.Instance);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request);
    }

    private sealed class FixedTimeProvider(DateTimeOffset timestamp) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => timestamp;
    }
}
