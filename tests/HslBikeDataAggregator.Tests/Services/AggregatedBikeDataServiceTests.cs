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
                    "stationId": "station-001",
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
        Assert.Equal("station-001", station.Id);
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
            .Setup(storage => storage.GetRecentSnapshotsAsync(CancellationToken))
            .ReturnsAsync([
                new StationSnapshot
                {
                    Timestamp = new DateTimeOffset(2026, 4, 4, 9, 45, 0, TimeSpan.Zero),
                    BikeCounts = new Dictionary<string, int>
                    {
                        ["station-001"] = 8
                    }
                }
            ]);

        var service = CreateService(blobStorage, EmptyStationsResponse);

        var result = await service.GetSnapshotsAsync(CancellationToken);

        var snapshot = Assert.Single(result);
        Assert.Equal(8, snapshot.BikeCounts["station-001"]);
    }

    [Fact]
    public async Task GetSnapshotsAsync_ReturnsEmptyCollectionWhenBlobStorageHasNoSnapshots()
    {
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetRecentSnapshotsAsync(CancellationToken))
            .ReturnsAsync([]);

        var service = CreateService(blobStorage, EmptyStationsResponse);

        var result = await service.GetSnapshotsAsync(CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAvailabilityAsync_ReturnsAvailabilityProfileFromBlobStorage()
    {
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetAvailabilityProfileAsync("station-001", CancellationToken))
            .ReturnsAsync([
                new HourlyAvailability
                {
                    Hour = 10,
                    AverageBikesAvailable = 7.5
                }
            ]);

        var service = CreateService(blobStorage, EmptyStationsResponse);

        var result = await service.GetAvailabilityAsync("station-001", CancellationToken);

        var availability = Assert.Single(result);
        Assert.Equal(10, availability.Hour);
        Assert.Equal(7.5, availability.AverageBikesAvailable);
    }

    [Fact]
    public async Task GetDestinationsAsync_ReturnsDestinationsFromBlobStorage()
    {
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetStationDestinationsAsync("station-001", CancellationToken))
            .ReturnsAsync([
                new StationHistory
                {
                    DepartureStationId = "station-001",
                    ArrivalStationId = "station-002",
                    TripCount = 12,
                    AverageDurationSeconds = 425.5,
                    AverageDistanceMetres = 1_280.2
                }
            ]);

        var service = CreateService(blobStorage, EmptyStationsResponse);

        var result = await service.GetDestinationsAsync("station-001", CancellationToken);

        var destination = Assert.Single(result);
        Assert.Equal("station-002", destination.ArrivalStationId);
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
