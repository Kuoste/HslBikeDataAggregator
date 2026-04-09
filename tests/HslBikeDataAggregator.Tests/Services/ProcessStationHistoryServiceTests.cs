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
    public async Task ProcessAsync_UsesNewestAvailableMonthOnly()
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
                    Departure,Return,Departure station id,Return station id,Covered distance (m),Duration (sec.)
                    2024-07-01T08:15:00,2024-07-01T08:30:00,001,002,1200,300
                    2024-07-01T09:15:00,2024-07-01T09:35:00,001,002,1400,360
                    """),
                _ => CreateResponse(HttpStatusCode.NotFound)
            });
        });

        var blobStorage = new Mock<IBikeDataBlobStorage>();
        var writtenStatistics = new Dictionary<string, MonthlyStationStatistics>(StringComparer.Ordinal);
        blobStorage
            .Setup(storage => storage.GetMonthlyStatisticsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MonthlyStationStatistics?)null);
        blobStorage
            .Setup(storage => storage.ListMonthlyStatisticStationIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        blobStorage
            .Setup(storage => storage.WriteMonthlyStatisticsAsync(It.IsAny<string>(), It.IsAny<MonthlyStationStatistics>(), It.IsAny<CancellationToken>()))
            .Callback<string, MonthlyStationStatistics, CancellationToken>((stationId, statistics, _) => writtenStatistics[stationId] = statistics)
            .Returns(Task.CompletedTask);

        var service = CreateService(new HttpClient(handler), blobStorage.Object);

        var result = await service.ProcessAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.SourceCount);
        Assert.Equal(2, result.JourneyCount);
        Assert.Equal(2, result.StationCount);
        Assert.DoesNotContain("https://example.test/2024-06.csv", requestedUrls, StringComparer.Ordinal);

        var station001Statistics = writtenStatistics["smoove:001"];
        Assert.Equal("2024-07", station001Statistics.Month);
        Assert.Equal(1, station001Statistics.Demand.DeparturesByHour[8]);
        Assert.Equal(1, station001Statistics.Demand.DeparturesByHour[9]);
        Assert.Equal(1, station001Statistics.Demand.WeekdayDeparturesByHour[8]);
        Assert.Equal(1, station001Statistics.Demand.WeekdayDeparturesByHour[9]);

        var station001Destination = Assert.Single(station001Statistics.Destinations.Rows);
        Assert.Equal("smoove:002", Assert.IsType<string>(station001Destination[0]));
        Assert.Equal(2, Assert.IsType<int>(station001Destination[1]));
        Assert.Equal(330, Assert.IsType<int>(station001Destination[2]));
        Assert.Equal(1300, Assert.IsType<int>(station001Destination[3]));

        var station002Statistics = writtenStatistics["smoove:002"];
        Assert.Equal(1, station002Statistics.Demand.ArrivalsByHour[8]);
        Assert.Equal(1, station002Statistics.Demand.ArrivalsByHour[9]);
        Assert.Empty(station002Statistics.Destinations.Rows);
    }

    [Fact]
    public async Task ProcessAsync_SearchesBackwardsAndDeletesStaleMonthlyStatistics()
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
                    Departure,Return,Departure station id,Return station id,Covered distance (m),Duration (sec.)
                    2024-06-03T08:15:00,2024-06-03T08:30:00,001,002,1200,300
                    2024-06-03T09:10:00,2024-06-03T09:25:00,002,001,900,240
                    """),
                _ => CreateResponse(HttpStatusCode.NotFound)
            });
        });

        var deletedStationIds = new List<string>();
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetMonthlyStatisticsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MonthlyStationStatistics?)null);
        blobStorage
            .Setup(storage => storage.ListMonthlyStatisticStationIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["smoove:001", "smoove:999"]);
        blobStorage
            .Setup(storage => storage.WriteMonthlyStatisticsAsync(It.IsAny<string>(), It.IsAny<MonthlyStationStatistics>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        blobStorage
            .Setup(storage => storage.DeleteMonthlyStatisticsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((stationId, _) => deletedStationIds.Add(stationId))
            .Returns(Task.CompletedTask);

        var service = CreateService(new HttpClient(handler), blobStorage.Object);

        var result = await service.ProcessAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.SourceCount);
        Assert.Equal(2, result.JourneyCount);
        Assert.Equal(2, result.StationCount);
        Assert.Contains("https://example.test/2024-07.csv", requestedUrls, StringComparer.Ordinal);
        Assert.Contains("https://example.test/2024-06.csv", requestedUrls, StringComparer.Ordinal);

        var deletedStationId = Assert.Single(deletedStationIds);
        Assert.Equal("smoove:999", deletedStationId);
    }

    [Fact]
    public async Task ProcessAsync_SkipsWriteWhenMonthAlreadyStored()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri?.ToString();
            Assert.NotNull(url);

            return Task.FromResult(url switch
            {
                "https://example.test/2024-07.csv" => CreateResponse(
                    HttpStatusCode.OK,
                    """
                    Departure,Return,Departure station id,Return station id,Covered distance (m),Duration (sec.)
                    2024-07-01T08:15:00,2024-07-01T08:30:00,001,002,1200,300
                    """),
                _ => CreateResponse(HttpStatusCode.NotFound)
            });
        });

        var writtenStationIds = new List<string>();
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetMonthlyStatisticsAsync("smoove:001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMonthlyStatistics("2024-07"));
        blobStorage
            .Setup(storage => storage.GetMonthlyStatisticsAsync("smoove:002", It.IsAny<CancellationToken>()))
            .ReturnsAsync((MonthlyStationStatistics?)null);
        blobStorage
            .Setup(storage => storage.ListMonthlyStatisticStationIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["smoove:001", "smoove:002"]);
        blobStorage
            .Setup(storage => storage.WriteMonthlyStatisticsAsync(It.IsAny<string>(), It.IsAny<MonthlyStationStatistics>(), It.IsAny<CancellationToken>()))
            .Callback<string, MonthlyStationStatistics, CancellationToken>((stationId, _, _) => writtenStationIds.Add(stationId))
            .Returns(Task.CompletedTask);

        var service = CreateService(new HttpClient(handler), blobStorage.Object);

        var result = await service.ProcessAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.SourceCount);
        Assert.Equal(1, result.JourneyCount);
        Assert.Equal(2, result.StationCount);

        var writtenStationId = Assert.Single(writtenStationIds);
        Assert.Equal("smoove:002", writtenStationId);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsEmptyResultWhenNoRecentMonthsAreAvailable()
    {
        var handler = new StubHttpMessageHandler(_ => Task.FromResult(CreateResponse(HttpStatusCode.NotFound)));
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        var service = CreateService(new HttpClient(handler), blobStorage.Object, availabilityProbeMonthCount: 3);

        var result = await service.ProcessAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, result.SourceCount);
        Assert.Equal(0, result.JourneyCount);
        Assert.Equal(0, result.StationCount);

        blobStorage.Verify(storage => storage.WriteMonthlyStatisticsAsync(It.IsAny<string>(), It.IsAny<MonthlyStationStatistics>(), It.IsAny<CancellationToken>()), Times.Never);
        blobStorage.Verify(storage => storage.ListMonthlyStatisticStationIdsAsync(It.IsAny<CancellationToken>()), Times.Never);
        blobStorage.Verify(storage => storage.DeleteMonthlyStatisticsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("http://example.test/{0:yyyy-MM}.csv", "example.test", "HTTP scheme")]
    [InlineData("https://evil.example.com/{0:yyyy-MM}.csv", "example.test", "wrong host")]
    [InlineData("ftp://example.test/{0:yyyy-MM}.csv", "example.test", "FTP scheme")]
    public async Task ProcessAsync_RejectsUrlWithInvalidSchemeOrHost(string urlPattern, string allowedHost, string reason)
    {
        _ = reason;

        var handler = new StubHttpMessageHandler(_ =>
            Task.FromResult(CreateResponse(HttpStatusCode.OK,
                """
                Departure,Return,Departure station id,Return station id,Covered distance (m),Duration (sec.)
                2024-07-01T08:15:00,2024-07-01T08:30:00,001,002,1200,300
                """)));

        var options = Options.Create(new HistoryProcessingOptions
        {
            TripHistoryUrlPattern = urlPattern,
            AllowedTripHistoryHost = allowedHost,
            RollingWindowMonthCount = 1,
            AvailabilityProbeMonthCount = 1
        });

        var blobStorage = new Mock<IBikeDataBlobStorage>();
        var service = new ProcessStationHistoryService(
            new HttpClient(handler),
            options,
            blobStorage.Object,
            new FixedTimeProvider(new DateTimeOffset(2024, 7, 15, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ProcessStationHistoryService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProcessAsync(TestContext.Current.CancellationToken));
    }

    private static ProcessStationHistoryService CreateService(
        HttpClient httpClient,
        IBikeDataBlobStorage blobStorage,
        int availabilityProbeMonthCount = 6)
        => new(
            httpClient,
            Options.Create(new HistoryProcessingOptions
            {
                TripHistoryUrlPattern = "https://example.test/{0:yyyy-MM}.csv",
                AllowedTripHistoryHost = "example.test",
                RollingWindowMonthCount = 1,
                AvailabilityProbeMonthCount = availabilityProbeMonthCount
            }),
            blobStorage,
            new FixedTimeProvider(new DateTimeOffset(2024, 7, 15, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ProcessStationHistoryService>.Instance);

    private static MonthlyStationStatistics CreateMonthlyStatistics(string month)
        => new()
        {
            Month = month,
            Demand = DemandProfile.Empty,
            Destinations = new ColumnarTable
            {
                Fields = ["arrivalStationId", "tripCount", "averageDurationSeconds", "averageDistanceMetres"],
                Rows = []
            }
        };

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
