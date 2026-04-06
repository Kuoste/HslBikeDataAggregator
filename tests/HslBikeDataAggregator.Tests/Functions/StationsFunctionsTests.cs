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
    public async Task GetStations_ReturnsOkResponseWithCorsAndCacheHeaders()
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
                """),
            NullLogger<StationsFunctions>.Instance);

        var responseData = await function.GetStations(request.Object, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, responseData.StatusCode);
        Assert.True(responseData.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Contains("https://kuoste.github.io", origins);
        Assert.True(responseData.Headers.TryGetValues("Cache-Control", out var cacheControl));
        Assert.Contains("public, max-age=120", cacheControl);

        responseData.Body.Position = 0;
        using var reader = new StreamReader(responseData.Body);
        Assert.Equal("[{\"id\":\"station-001\",\"name\":\"Central Station\",\"lat\":60.1708,\"lon\":24.941,\"capacity\":24,\"bikesAvailable\":8,\"spacesAvailable\":16,\"isActive\":true}]", await reader.ReadToEndAsync(cancellationToken));
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
            .Setup(storage => storage.GetRecentSnapshotsAsync(cancellationToken))
            .ReturnsAsync([
                new StationSnapshot
                {
                    Timestamp = new DateTimeOffset(2026, 4, 4, 9, 45, 0, TimeSpan.Zero),
                    BikeCounts = new Dictionary<string, int>
                    {
                        ["station-001"] = 8,
                        ["station-002"] = 3
                    }
                }
            ]);

        var function = new StationsFunctions(CreateBikeDataService(blobStorage), NullLogger<StationsFunctions>.Instance);

        var responseData = await function.GetSnapshots(request.Object, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, responseData.StatusCode);
        Assert.True(responseData.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Contains("https://kuoste.github.io", origins);
        Assert.True(responseData.Headers.TryGetValues("Cache-Control", out var cacheControl));
        Assert.Contains("public, max-age=900", cacheControl);

        responseData.Body.Position = 0;
        using var reader = new StreamReader(responseData.Body);
        Assert.Equal("[{\"timestamp\":\"2026-04-04T09:45:00+00:00\",\"bikeCounts\":{\"station-001\":8,\"station-002\":3}}]", await reader.ReadToEndAsync(cancellationToken));
    }

    [Fact]
    public async Task GetStationAvailability_ReturnsOkResponseWithStoredAvailabilityProfile()
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
        request.SetupGet(httpRequest => httpRequest.Url).Returns(new Uri("https://localhost/api/stations/station-001/availability"));

        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetAvailabilityProfileAsync("station-001", cancellationToken))
            .ReturnsAsync([
                new HourlyAvailability
                {
                    Hour = 10,
                    AverageBikesAvailable = 7.5
                },
                new HourlyAvailability
                {
                    Hour = 11,
                    AverageBikesAvailable = 5.25
                }
            ]);

        var function = new StationsFunctions(CreateBikeDataService(blobStorage), NullLogger<StationsFunctions>.Instance);

        var responseData = await function.GetStationAvailability(request.Object, "station-001", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, responseData.StatusCode);
        Assert.True(responseData.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Contains("https://kuoste.github.io", origins);
        Assert.True(responseData.Headers.TryGetValues("Cache-Control", out var cacheControl));
        Assert.Contains("public, max-age=3600", cacheControl);

        responseData.Body.Position = 0;
        using var reader = new StreamReader(responseData.Body);
        Assert.Equal("[{\"hour\":10,\"averageBikesAvailable\":7.5},{\"hour\":11,\"averageBikesAvailable\":5.25}]", await reader.ReadToEndAsync(cancellationToken));
    }

    [Fact]
    public async Task GetStationDestinations_ReturnsOkResponseWithStoredDestinations()
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
        request.SetupGet(httpRequest => httpRequest.Url).Returns(new Uri("https://localhost/api/stations/station-001/destinations"));

        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetStationDestinationsAsync("station-001", cancellationToken))
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

        var function = new StationsFunctions(CreateBikeDataService(blobStorage), NullLogger<StationsFunctions>.Instance);

        var responseData = await function.GetStationDestinations(request.Object, "station-001", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, responseData.StatusCode);
        Assert.True(responseData.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Contains("https://kuoste.github.io", origins);
        Assert.True(responseData.Headers.TryGetValues("Cache-Control", out var cacheControl));
        Assert.Contains("public, max-age=3600", cacheControl);

        responseData.Body.Position = 0;
        using var reader = new StreamReader(responseData.Body);
        Assert.Equal("[{\"departureStationId\":\"station-001\",\"arrivalStationId\":\"station-002\",\"tripCount\":12,\"averageDurationSeconds\":425.5,\"averageDistanceMetres\":1280.2}]", await reader.ReadToEndAsync(cancellationToken));
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
