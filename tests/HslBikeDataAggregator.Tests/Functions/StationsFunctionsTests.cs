using System.Net;
using System.Text;
using System.Security.Claims;

using Azure.Core.Serialization;

using HslBikeDataAggregator.Configuration;
using HslBikeDataAggregator.Functions;
using HslBikeDataAggregator.Models;
using HslBikeDataAggregator.Services;
using HslBikeDataAggregator.Storage;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

namespace HslBikeDataAggregator.Tests.Functions;

public sealed class StationsFunctionsTests
{
    [Fact]
    public async Task GetStations_ReturnsOkResponseWithCacheHeaders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services
            .AddOptions<WorkerOptions>()
            .Configure(options => options.Serializer = new JsonObjectSerializer());

        var serviceProvider = services.BuildServiceProvider();

        var functionContext = new Mock<FunctionContext>();
        functionContext.SetupProperty(context => context.InstanceServices, serviceProvider);

        var responseHeaders = new HttpHeadersCollection();
        var responseBody = new MemoryStream();
        var response = new Mock<HttpResponseData>(functionContext.Object);
        response.SetupProperty(httpResponse => httpResponse.StatusCode);
        response.SetupProperty(httpResponse => httpResponse.Body, responseBody);
        response.SetupGet(httpResponse => httpResponse.Headers).Returns(responseHeaders);
        response.SetupGet(httpResponse => httpResponse.Cookies).Returns(Mock.Of<HttpCookies>());

        var request = new Mock<HttpRequestData>(functionContext.Object);
        request.Setup(httpRequest => httpRequest.CreateResponse()).Returns(response.Object);
        request.SetupGet(httpRequest => httpRequest.Body).Returns(Stream.Null);
        request.SetupGet(httpRequest => httpRequest.Headers).Returns(new HttpHeadersCollection());
        request.SetupGet(httpRequest => httpRequest.Identities).Returns(Array.Empty<ClaimsIdentity>());
        request.SetupGet(httpRequest => httpRequest.Method).Returns("GET");
        request.SetupGet(httpRequest => httpRequest.Url).Returns(new Uri("https://localhost/api/stations"));

        var blobStorage = new Mock<IBikeDataBlobStorage>();

        var function = new StationsFunctions(
            CreateBikeDataService(
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
                """),
            NullLogger<StationsFunctions>.Instance);

        var responseData = await function.GetStations(request.Object, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, responseData.StatusCode);
        Assert.True(responseData.Headers.TryGetValues("Cache-Control", out var cacheControl));
        Assert.Contains("public, max-age=30", cacheControl);

        responseData.Body.Position = 0;
        using var reader = new StreamReader(responseData.Body);
        Assert.Equal("[{\"id\":\"smoove:001\",\"name\":\"Central Station\",\"lat\":60.1708,\"lon\":24.941,\"capacity\":24,\"bikesAvailable\":8,\"spacesAvailable\":16,\"isActive\":true}]", await reader.ReadToEndAsync(cancellationToken));
    }

    [Fact]
    public async Task GetSnapshots_ReturnsOkResponseWithStoredSnapshots()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services
            .AddOptions<WorkerOptions>()
            .Configure(options => options.Serializer = new JsonObjectSerializer());

        var serviceProvider = services.BuildServiceProvider();

        var functionContext = new Mock<FunctionContext>();
        functionContext.SetupProperty(context => context.InstanceServices, serviceProvider);

        var responseHeaders = new HttpHeadersCollection();
        var responseBody = new MemoryStream();
        var response = new Mock<HttpResponseData>(functionContext.Object);
        response.SetupProperty(httpResponse => httpResponse.StatusCode);
        response.SetupProperty(httpResponse => httpResponse.Body, responseBody);
        response.SetupGet(httpResponse => httpResponse.Headers).Returns(responseHeaders);
        response.SetupGet(httpResponse => httpResponse.Cookies).Returns(Mock.Of<HttpCookies>());

        var request = new Mock<HttpRequestData>(functionContext.Object);
        request.Setup(httpRequest => httpRequest.CreateResponse()).Returns(response.Object);
        request.SetupGet(httpRequest => httpRequest.Body).Returns(Stream.Null);
        request.SetupGet(httpRequest => httpRequest.Headers).Returns(new HttpHeadersCollection());
        request.SetupGet(httpRequest => httpRequest.Identities).Returns(Array.Empty<ClaimsIdentity>());
        request.SetupGet(httpRequest => httpRequest.Method).Returns("GET");
        request.SetupGet(httpRequest => httpRequest.Url).Returns(new Uri("https://localhost/api/snapshots"));

        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetSnapshotTimeSeriesAsync(cancellationToken))
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
                    },
                    new StationCountSeries
                    {
                        StationId = "smoove:002",
                        Counts = [3]
                    }
                ]
            });

        var function = new StationsFunctions(CreateBikeDataService(blobStorage), NullLogger<StationsFunctions>.Instance);

        var responseData = await function.GetSnapshots(request.Object, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, responseData.StatusCode);
        Assert.True(responseData.Headers.TryGetValues("Cache-Control", out var cacheControl));
        Assert.Contains("public, max-age=900", cacheControl);

        responseData.Body.Position = 0;
        using var reader = new StreamReader(responseData.Body);
        Assert.Equal("{\"intervalMinutes\":15,\"timestamps\":[\"2026-04-04T09:45:00Z\"],\"rows\":[[\"smoove:001\",8],[\"smoove:002\",3]]}", await reader.ReadToEndAsync(cancellationToken));
    }

    [Fact]
    public async Task GetStationStatistics_ReturnsOkResponseWithStoredStatistics()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services
            .AddOptions<WorkerOptions>()
            .Configure(options => options.Serializer = new JsonObjectSerializer());

        var serviceProvider = services.BuildServiceProvider();

        var functionContext = new Mock<FunctionContext>();
        functionContext.SetupProperty(context => context.InstanceServices, serviceProvider);

        var responseHeaders = new HttpHeadersCollection();
        var responseBody = new MemoryStream();
        var response = new Mock<HttpResponseData>(functionContext.Object);
        response.SetupProperty(httpResponse => httpResponse.StatusCode);
        response.SetupProperty(httpResponse => httpResponse.Body, responseBody);
        response.SetupGet(httpResponse => httpResponse.Headers).Returns(responseHeaders);
        response.SetupGet(httpResponse => httpResponse.Cookies).Returns(Mock.Of<HttpCookies>());

        var request = new Mock<HttpRequestData>(functionContext.Object);
        request.Setup(httpRequest => httpRequest.CreateResponse()).Returns(response.Object);
        request.SetupGet(httpRequest => httpRequest.Body).Returns(Stream.Null);
        request.SetupGet(httpRequest => httpRequest.Headers).Returns(new HttpHeadersCollection());
        request.SetupGet(httpRequest => httpRequest.Identities).Returns(Array.Empty<ClaimsIdentity>());
        request.SetupGet(httpRequest => httpRequest.Method).Returns("GET");
        request.SetupGet(httpRequest => httpRequest.Url).Returns(new Uri("https://localhost/api/stations/smoove:001/statistics"));

        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetMonthlyStatisticsAsync("smoove:001", cancellationToken))
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
                        new object[] { "smoove:002", 12, 426, 1280 }
                    ]
                }
            });

        var function = new StationsFunctions(CreateBikeDataService(blobStorage), NullLogger<StationsFunctions>.Instance);

        var responseData = await function.GetStationStatistics(request.Object, "smoove:001", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, responseData.StatusCode);
        Assert.True(responseData.Headers.TryGetValues("Cache-Control", out var cacheControl));
        Assert.Contains("public, max-age=3600", cacheControl);

        responseData.Body.Position = 0;
        using var reader = new StreamReader(responseData.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);
        Assert.Contains("\"month\":\"2026-04\"", body, StringComparison.Ordinal);
        Assert.Contains("\"fields\":[\"arrivalStationId\",\"tripCount\",\"averageDurationSeconds\",\"averageDistanceMetres\"]", body, StringComparison.Ordinal);
        Assert.Contains("[\"smoove:002\",12,426,1280]", body, StringComparison.Ordinal);
    }

    private const string EmptyStationsResponse = """
        {
          "data": {
            "vehicleRentalStations": []
          }
        }
        """;

    private static AggregatedBikeDataService CreateBikeDataService(Mock<IBikeDataBlobStorage> blobStorage, string liveStationsResponse = EmptyStationsResponse)
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
