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

    [Fact]
    public async Task PollAsync_NewStationBackfillsWithSentinel_WhenTimeSeriesAlreadyHasMultipleTimestamps()
    {
        var handler = new StubHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
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
                        "availableVehicles": { "byType": [{ "count": 5 }] },
                        "availableSpaces": { "byType": [{ "count": 3 }] }
                      },
                      {
                        "stationId": "smoove:002",
                        "name": "New Station",
                        "lat": 60.172,
                        "lon": 24.943,
                        "allowPickup": true,
                        "allowDropoff": true,
                        "capacity": 10,
                        "availableVehicles": { "byType": [{ "count": 4 }] },
                        "availableSpaces": { "byType": [{ "count": 6 }] }
                      }
                    ]
                  }
                }
                """,
                Encoding.UTF8,
                "application/json")
        }));

        var options = Options.Create(new PollStationsOptions
        {
            DigitransitSubscriptionKey = "test-key",
            SnapshotHistoryLimit = 5
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
                    new StationCountSeries { StationId = "smoove:001", Counts = [3, 4] }
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

        await service.PollAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(writtenSnapshots);
        Assert.Equal(3, writtenSnapshots!.Timestamps.Count);
        Assert.Equal(2, writtenSnapshots.Stations.Count);

        var station001 = writtenSnapshots.Stations.Single(s => s.StationId == "smoove:001");
        Assert.Equal([3, 4, 5], station001.Counts);

        var station002 = writtenSnapshots.Stations.Single(s => s.StationId == "smoove:002");
        Assert.Equal([-1, -1, 4], station002.Counts);
    }

    [Fact]
    public async Task PollAsync_RecoverFromEmptyRows_BackfillsAllStationsWithSentinel()
    {
        var handler = new StubHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
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
                        "availableVehicles": { "byType": [{ "count": 7 }] },
                        "availableSpaces": { "byType": [{ "count": 5 }] }
                      }
                    ]
                  }
                }
                """,
                Encoding.UTF8,
                "application/json")
        }));

        var options = Options.Create(new PollStationsOptions
        {
            DigitransitSubscriptionKey = "test-key",
            SnapshotHistoryLimit = 5
        });
        var digitransitStationClient = new DigitransitStationClient(new HttpClient(handler), options);
        var blobStorage = new Mock<IBikeDataBlobStorage>();

        // Simulate the broken blob state: timestamps present but rows empty
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
                Stations = []
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

        await service.PollAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(writtenSnapshots);
        Assert.Equal(3, writtenSnapshots!.Timestamps.Count);

        var station001 = Assert.Single(writtenSnapshots.Stations);
        Assert.Equal("smoove:001", station001.StationId);
        Assert.Equal([-1, -1, 7], station001.Counts);
    }

    [Fact]
    public async Task PollAsync_DoesNotWriteBlob_WhenApiReturnsNoStations()
    {
        var handler = new StubHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"data":{"vehicleRentalStations":[]}}""",
                Encoding.UTF8,
                "application/json")
        }));

        var options = Options.Create(new PollStationsOptions
        {
            DigitransitSubscriptionKey = "test-key",
            SnapshotHistoryLimit = 5,
            EmptyResponseRetryCount = 0
        });
        var digitransitStationClient = new DigitransitStationClient(new HttpClient(handler), options);
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetSnapshotTimeSeriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SnapshotTimeSeries.Empty);

        var timestamp = new DateTimeOffset(2026, 4, 3, 10, 10, 0, TimeSpan.Zero);
        var service = new PollStationsService(
            digitransitStationClient,
            blobStorage.Object,
            options,
            new FixedTimeProvider(timestamp),
            NullLogger<PollStationsService>.Instance);

        var result = await service.PollAsync(TestContext.Current.CancellationToken);

        blobStorage.Verify(
            storage => storage.WriteSnapshotTimeSeriesAsync(It.IsAny<SnapshotTimeSeries>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Equal(0, result.StationCount);
        Assert.Equal(0, result.SnapshotCount);
    }

    [Fact]
    public async Task PollAsync_RetriesAndSucceeds_WhenApiReturnsEmptyThenValidData()
    {
        var callCount = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            callCount++;
            var json = callCount < 3
                ? """{ "data": { "vehicleRentalStations": [] } }"""
                : """
                  {
                    "data": {
                      "vehicleRentalStations": [
                        {
                          "stationId": "smoove:001",
                          "name": "Central Station",
                          "lat": 60.17,
                          "lon": 24.94,
                          "allowPickup": true,
                          "allowDropoff": true,
                          "capacity": 24,
                          "availableVehicles": { "byType": [{ "count": 5 }] },
                          "availableSpaces": { "byType": [{ "count": 3 }] }
                        }
                      ]
                    }
                  }
                  """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });

        var options = Options.Create(new PollStationsOptions
        {
            DigitransitSubscriptionKey = "test-key",
            SnapshotHistoryLimit = 5,
            EmptyResponseRetryCount = 3,
            EmptyResponseRetryDelaySeconds = 1
        });
        var digitransitStationClient = new DigitransitStationClient(new HttpClient(handler), options);
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetSnapshotTimeSeriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SnapshotTimeSeries.Empty);
        blobStorage
            .Setup(storage => storage.WriteSnapshotTimeSeriesAsync(It.IsAny<SnapshotTimeSeries>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var timestamp = new DateTimeOffset(2026, 4, 3, 10, 10, 0, TimeSpan.Zero);
        var service = new PollStationsService(
            digitransitStationClient,
            blobStorage.Object,
            options,
            new FixedTimeProvider(timestamp),
            NullLogger<PollStationsService>.Instance);

        var result = await service.PollAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, callCount);
        Assert.Equal(1, result.StationCount);
        blobStorage.Verify(
            storage => storage.WriteSnapshotTimeSeriesAsync(It.IsAny<SnapshotTimeSeries>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PollAsync_SkipsWrite_WhenAllRetriesExhausted()
    {
        var callCount = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            callCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{ "data": { "vehicleRentalStations": [] } }""",
                    Encoding.UTF8,
                    "application/json")
            });
        });

        var options = Options.Create(new PollStationsOptions
        {
            DigitransitSubscriptionKey = "test-key",
            SnapshotHistoryLimit = 5,
            EmptyResponseRetryCount = 2,
            EmptyResponseRetryDelaySeconds = 1
        });
        var digitransitStationClient = new DigitransitStationClient(new HttpClient(handler), options);
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetSnapshotTimeSeriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SnapshotTimeSeries.Empty);

        var timestamp = new DateTimeOffset(2026, 4, 3, 10, 10, 0, TimeSpan.Zero);
        var service = new PollStationsService(
            digitransitStationClient,
            blobStorage.Object,
            options,
            new FixedTimeProvider(timestamp),
            NullLogger<PollStationsService>.Instance);

        var result = await service.PollAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, callCount); // 1 initial + 2 retries
        Assert.Equal(0, result.StationCount);
        Assert.Equal(0, result.SnapshotCount);
        blobStorage.Verify(
            storage => storage.WriteSnapshotTimeSeriesAsync(It.IsAny<SnapshotTimeSeries>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request);
    }

    private sealed class FixedTimeProvider(DateTimeOffset timestamp) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => timestamp;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            // Fire the callback immediately so Task.Delay completes without waiting.
            callback(state);
            return new NoOpTimer();
        }

        private sealed class NoOpTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => false;
            public void Dispose() { }
            public ValueTask DisposeAsync() => default;
        }
    }
}
