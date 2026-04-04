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

public sealed class PollStationsServiceTests
{
    [Fact]
    public async Task PollAsync_StoresLatestStationsAndTrimsSnapshotHistory()
    {
        var capturedRequestBody = string.Empty;
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            capturedRequest = request;
            capturedRequestBody = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "data": {
                        "vehicleRentalStations": [
                          {
                            "stationId": "001",
                            "name": "Central Station",
                            "lat": 60.1708,
                            "lon": 24.941,
                            "allowPickup": true,
                            "allowDropoff": true,
                            "capacity": 24,
                            "availableVehicles": {
                              "byType": [
                                { "count": 7 },
                                { "count": 2 }
                              ]
                            },
                            "availableSpaces": {
                              "byType": [
                                { "count": 5 }
                              ]
                            }
                          }
                        ]
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var options = Options.Create(new PollStationsOptions
        {
            DigitransitSubscriptionKey = "test-subscription-key",
            SnapshotHistoryLimit = 2
        });
        var digitransitStationClient = new DigitransitStationClient(new HttpClient(handler), options);
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetRecentSnapshotsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new StationSnapshot
                {
                    Timestamp = new DateTimeOffset(2026, 4, 3, 10, 0, 0, TimeSpan.Zero),
                    BikeCounts = new Dictionary<string, int> { ["old"] = 3 }
                },
                new StationSnapshot
                {
                    Timestamp = new DateTimeOffset(2026, 4, 3, 10, 5, 0, TimeSpan.Zero),
                    BikeCounts = new Dictionary<string, int> { ["older"] = 4 }
                }
            ]);

        IReadOnlyList<BikeStation>? latestStations = null;
        blobStorage
            .Setup(storage => storage.WriteLatestStationsAsync(It.IsAny<IReadOnlyList<BikeStation>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<BikeStation>, CancellationToken>((stations, _) => latestStations = stations)
            .Returns(Task.CompletedTask);

        IReadOnlyList<StationSnapshot>? writtenSnapshots = null;
        blobStorage
            .Setup(storage => storage.WriteRecentSnapshotsAsync(It.IsAny<IReadOnlyList<StationSnapshot>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<StationSnapshot>, CancellationToken>((snapshots, _) => writtenSnapshots = snapshots)
            .Returns(Task.CompletedTask);

        var writtenAvailabilityProfiles = new Dictionary<string, IReadOnlyList<HourlyAvailability>>(StringComparer.Ordinal);
        blobStorage
            .Setup(storage => storage.WriteAvailabilityProfileAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<HourlyAvailability>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<HourlyAvailability>, CancellationToken>((stationId, availabilityProfile, _) => writtenAvailabilityProfiles[stationId] = availabilityProfile)
            .Returns(Task.CompletedTask);

        var timestamp = new DateTimeOffset(2026, 4, 3, 10, 10, 0, TimeSpan.Zero);
        var availabilityProfileService = new AvailabilityProfileService();
        var service = new PollStationsService(
            digitransitStationClient,
            blobStorage.Object,
            availabilityProfileService,
            options,
            new FixedTimeProvider(timestamp),
            NullLogger<PollStationsService>.Instance);

        var result = await service.PollAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.True(capturedRequest.Headers.TryGetValues("digitransit-subscription-key", out var headerValues));
        Assert.Contains("test-subscription-key", headerValues);
        Assert.Contains("vehicleRentalStations", capturedRequestBody);

        var station = Assert.Single(latestStations!);
        Assert.Equal("001", station.Id);
        Assert.Equal("Central Station", station.Name);
        Assert.Equal(9, station.BikesAvailable);
        Assert.Equal(5, station.SpacesAvailable);
        Assert.True(station.IsActive);

        Assert.NotNull(writtenSnapshots);
        Assert.Equal(2, writtenSnapshots!.Count);
        Assert.Equal(new DateTimeOffset(2026, 4, 3, 10, 5, 0, TimeSpan.Zero), writtenSnapshots[0].Timestamp);
        Assert.Equal(timestamp, writtenSnapshots[1].Timestamp);
        Assert.Equal(9, writtenSnapshots[1].BikeCounts["001"]);

        var station001Profile = Assert.IsAssignableFrom<IReadOnlyList<HourlyAvailability>>(writtenAvailabilityProfiles["001"]);
        var station001Availability = Assert.Single(station001Profile);
        Assert.Equal(10, station001Availability.Hour);
        Assert.Equal(9, station001Availability.AverageBikesAvailable);

        Assert.Equal(timestamp, result.Timestamp);
        Assert.Equal(1, result.StationCount);
        Assert.Equal(2, result.SnapshotCount);
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
