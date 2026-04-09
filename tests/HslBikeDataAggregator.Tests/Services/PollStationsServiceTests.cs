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
    public async Task PollAsync_StoresSnapshotsAndTrimsSnapshotHistory()
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
                            "stationId": "smoove:001",
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
            .Setup(storage => storage.GetSnapshotTimeSeriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SnapshotTimeSeries
            {
                IntervalMinutes = 5,
                Timestamps =
                [
                    new DateTimeOffset(2026, 4, 3, 10, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 4, 3, 10, 5, 0, TimeSpan.Zero)
                ],
                Stations =
                [
                    new StationCountSeries
                    {
                        StationId = "smoove:001",
                        Counts = [6, 8]
                    }
                ]
            });

        SnapshotTimeSeries? writtenSnapshots = null;
        blobStorage
            .Setup(storage => storage.WriteSnapshotTimeSeriesAsync(It.IsAny<SnapshotTimeSeries>(), It.IsAny<CancellationToken>()))
            .Callback<SnapshotTimeSeries, CancellationToken>((snapshots, _) => writtenSnapshots = snapshots)
            .Returns(Task.CompletedTask);

        var timestamp = new DateTimeOffset(2026, 4, 3, 10, 10, 0, TimeSpan.Zero);
        var service = new PollStationsService(
            digitransitStationClient,
            blobStorage.Object,
            options,
            new FixedTimeProvider(timestamp),
            NullLogger<PollStationsService>.Instance);

        var result = await service.PollAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.True(capturedRequest.Headers.TryGetValues("digitransit-subscription-key", out var headerValues));
        Assert.Contains("test-subscription-key", headerValues);
        Assert.Contains("vehicleRentalStations", capturedRequestBody);

        Assert.NotNull(writtenSnapshots);
        Assert.Equal(2, writtenSnapshots!.Timestamps.Count);
        Assert.Equal(new DateTimeOffset(2026, 4, 3, 10, 5, 0, TimeSpan.Zero), writtenSnapshots.Timestamps[0]);
        Assert.Equal(timestamp, writtenSnapshots.Timestamps[1]);
        Assert.Equal(5, writtenSnapshots.IntervalMinutes);

        var station001Series = Assert.Single(writtenSnapshots.Stations);
        Assert.Equal("smoove:001", station001Series.StationId);
        Assert.Equal([8, 9], station001Series.Counts);

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
