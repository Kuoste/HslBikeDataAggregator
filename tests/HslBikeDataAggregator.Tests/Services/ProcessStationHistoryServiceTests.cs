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
    public async Task ProcessAsync_AggregatesTripsByDepartureStationAndWritesSortedDestinations()
    {
        var responsesByUrl = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["https://example.test/history-1.csv"] =
                """
                Departure station id,Return station id,Covered distance (m),Duration (sec.)
                001,002,1200,300
                001,002,1400,360
                """,
            ["https://example.test/history-2.csv"] =
                """
                Departure station id,Return station id,Covered distance (m),Duration (sec.)
                001,003,800,240
                002,001,1000,420
                """
        };

        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri?.ToString();
            Assert.NotNull(url);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responsesByUrl[url!], Encoding.UTF8, "text/csv")
            });
        });

        var options = Options.Create(new HistoryProcessingOptions
        {
            TripHistoryUrls = ["https://example.test/history-1.csv", "https://example.test/history-2.csv"]
        });

        var writtenDestinations = new Dictionary<string, IReadOnlyList<StationHistory>>(StringComparer.Ordinal);
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.WriteStationDestinationsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<StationHistory>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<StationHistory>, CancellationToken>((stationId, destinations, _) => writtenDestinations[stationId] = destinations)
            .Returns(Task.CompletedTask);

        var service = new ProcessStationHistoryService(
            new HttpClient(handler),
            options,
            blobStorage.Object,
            NullLogger<ProcessStationHistoryService>.Instance);

        var result = await service.ProcessAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.SourceCount);
        Assert.Equal(4, result.JourneyCount);
        Assert.Equal(2, result.StationCount);

        var station001Destinations = Assert.IsAssignableFrom<IReadOnlyList<StationHistory>>(writtenDestinations["001"]);
        Assert.Collection(
            station001Destinations,
            destination =>
            {
                Assert.Equal("001", destination.DepartureStationId);
                Assert.Equal("002", destination.ArrivalStationId);
                Assert.Equal(2, destination.TripCount);
                Assert.Equal(330, destination.AverageDurationSeconds);
                Assert.Equal(1300, destination.AverageDistanceMetres);
            },
            destination =>
            {
                Assert.Equal("001", destination.DepartureStationId);
                Assert.Equal("003", destination.ArrivalStationId);
                Assert.Equal(1, destination.TripCount);
                Assert.Equal(240, destination.AverageDurationSeconds);
                Assert.Equal(800, destination.AverageDistanceMetres);
            });

        var station002Destinations = Assert.IsAssignableFrom<IReadOnlyList<StationHistory>>(writtenDestinations["002"]);
        var station002Destination = Assert.Single(station002Destinations);
        Assert.Equal("001", station002Destination.ArrivalStationId);
        Assert.Equal(1, station002Destination.TripCount);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request);
    }
}
