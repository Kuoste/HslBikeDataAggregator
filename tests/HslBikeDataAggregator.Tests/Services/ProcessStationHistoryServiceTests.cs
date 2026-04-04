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

public sealed class ProcessStationHistoryServiceTests
{
    [Fact]
    public async Task ProcessAsync_UsesOnlyNewestTwoAvailableMonths()
    {
        var requestedUrls = new List<string>();
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri?.ToString();
            Assert.NotNull(url);
            requestedUrls.Add(url!);

            return Task.FromResult(url switch
            {
                "https://example.test/2024-07.csv" => CreateResponse(
                    HttpStatusCode.OK,
                    """
                    Departure station id,Return station id,Covered distance (m),Duration (sec.)
                    001,002,1200,300
                    """),
                "https://example.test/2024-06.csv" => CreateResponse(
                    HttpStatusCode.OK,
                    """
                    Departure station id,Return station id,Covered distance (m),Duration (sec.)
                    001,003,800,240
                    """),
                "https://example.test/2024-05.csv" => CreateResponse(
                    HttpStatusCode.OK,
                    """
                    Departure station id,Return station id,Covered distance (m),Duration (sec.)
                    001,004,900,260
                    """),
                _ => CreateResponse(HttpStatusCode.NotFound)
            });
        });

        var options = Options.Create(new HistoryProcessingOptions
        {
            TripHistoryUrlPattern = "https://example.test/{0:yyyy-MM}.csv",
            RollingWindowMonthCount = 2,
            AvailabilityProbeMonthCount = 6
        });

        var writtenDestinations = new Dictionary<string, IReadOnlyList<StationHistory>>(StringComparer.Ordinal);
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.ListStationDestinationIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        blobStorage
            .Setup(storage => storage.WriteStationDestinationsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<StationHistory>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<StationHistory>, CancellationToken>((stationId, destinations, _) => writtenDestinations[stationId] = destinations)
            .Returns(Task.CompletedTask);

        var service = new ProcessStationHistoryService(
            new HttpClient(handler),
            options,
            blobStorage.Object,
            new FixedTimeProvider(new DateTimeOffset(2024, 7, 15, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ProcessStationHistoryService>.Instance);

        var result = await service.ProcessAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.SourceCount);
        Assert.Equal(2, result.JourneyCount);
        Assert.Equal(1, result.StationCount);

        var station001Destinations = Assert.IsAssignableFrom<IReadOnlyList<StationHistory>>(writtenDestinations["001"]);
        Assert.Collection(
            station001Destinations,
            destination =>
            {
                Assert.Equal("002", destination.ArrivalStationId);
                Assert.Equal(1, destination.TripCount);
            },
            destination =>
            {
                Assert.Equal("003", destination.ArrivalStationId);
                Assert.Equal(1, destination.TripCount);
            });

        Assert.DoesNotContain("https://example.test/2024-05.csv", requestedUrls, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_SearchesBackwardsAndDeletesStaleDestinationProfiles()
    {
        var requestedUrls = new List<string>();
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri?.ToString();
            Assert.NotNull(url);
            requestedUrls.Add(url!);

            return Task.FromResult(url switch
            {
                "https://example.test/2024-07.csv" => CreateResponse(HttpStatusCode.NotFound),
                "https://example.test/2024-06.csv" => CreateResponse(
                    HttpStatusCode.OK,
                    """
                    Departure station id,Return station id,Covered distance (m),Duration (sec.)
                    001,002,1200,300
                    001,002,1400,360
                    """),
                "https://example.test/2024-05.csv" => CreateResponse(
                    HttpStatusCode.OK,
                    """
                    Departure station id,Return station id,Covered distance (m),Duration (sec.)
                    002,001,1000,420
                    """),
                _ => CreateResponse(HttpStatusCode.NotFound)
            });
        });

        var options = Options.Create(new HistoryProcessingOptions
        {
            TripHistoryUrlPattern = "https://example.test/{0:yyyy-MM}.csv",
            RollingWindowMonthCount = 2,
            AvailabilityProbeMonthCount = 6
        });

        var writtenDestinations = new Dictionary<string, IReadOnlyList<StationHistory>>(StringComparer.Ordinal);
        var deletedStationIds = new List<string>();
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.ListStationDestinationIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["001", "999"]);
        blobStorage
            .Setup(storage => storage.WriteStationDestinationsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<StationHistory>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<StationHistory>, CancellationToken>((stationId, destinations, _) => writtenDestinations[stationId] = destinations)
            .Returns(Task.CompletedTask);
        blobStorage
            .Setup(storage => storage.DeleteStationDestinationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((stationId, _) => deletedStationIds.Add(stationId))
            .Returns(Task.CompletedTask);

        var service = new ProcessStationHistoryService(
            new HttpClient(handler),
            options,
            blobStorage.Object,
            new FixedTimeProvider(new DateTimeOffset(2024, 7, 15, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ProcessStationHistoryService>.Instance);

        var result = await service.ProcessAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.SourceCount);
        Assert.Equal(3, result.JourneyCount);
        Assert.Equal(2, result.StationCount);

        var station001Destinations = Assert.IsAssignableFrom<IReadOnlyList<StationHistory>>(writtenDestinations["001"]);
        var station001Destination = Assert.Single(station001Destinations);
        Assert.Equal("002", station001Destination.ArrivalStationId);
        Assert.Equal(2, station001Destination.TripCount);
        Assert.Equal(330, station001Destination.AverageDurationSeconds);
        Assert.Equal(1300, station001Destination.AverageDistanceMetres);

        var station002Destinations = Assert.IsAssignableFrom<IReadOnlyList<StationHistory>>(writtenDestinations["002"]);
        var station002Destination = Assert.Single(station002Destinations);
        Assert.Equal("001", station002Destination.ArrivalStationId);

        var deletedStationId = Assert.Single(deletedStationIds);
        Assert.Equal("999", deletedStationId);

        Assert.Contains("https://example.test/2024-07.csv", requestedUrls, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsEmptyResultWhenNoRecentMonthsAreAvailable()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri?.ToString();
            Assert.NotNull(url);

            return Task.FromResult(CreateResponse(HttpStatusCode.NotFound));
        });

        var options = Options.Create(new HistoryProcessingOptions
        {
            TripHistoryUrlPattern = "https://example.test/{0:yyyy-MM}.csv",
            RollingWindowMonthCount = 2,
            AvailabilityProbeMonthCount = 3
        });

        var blobStorage = new Mock<IBikeDataBlobStorage>();
        var service = new ProcessStationHistoryService(
            new HttpClient(handler),
            options,
            blobStorage.Object,
            new FixedTimeProvider(new DateTimeOffset(2024, 7, 15, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ProcessStationHistoryService>.Instance);

        var result = await service.ProcessAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, result.SourceCount);
        Assert.Equal(0, result.JourneyCount);
        Assert.Equal(0, result.StationCount);

        blobStorage.Verify(storage => storage.WriteStationDestinationsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<StationHistory>>(), It.IsAny<CancellationToken>()), Times.Never);
        blobStorage.Verify(storage => storage.ListStationDestinationIdsAsync(It.IsAny<CancellationToken>()), Times.Never);
        blobStorage.Verify(storage => storage.DeleteStationDestinationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string? content = null)
    {
        var response = new HttpResponseMessage(statusCode);
        if (content is not null)
        {
            response.Content = new StringContent(content, Encoding.UTF8, "text/csv");
        }

        return response;
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
