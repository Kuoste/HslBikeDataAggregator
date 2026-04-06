using System.Net;
using System.Text;

using HslBikeDataAggregator.Configuration;
using HslBikeDataAggregator.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HslBikeDataAggregator.Tests.Services;

public sealed class LiveStationCacheServiceTests
{
    [Fact]
    public async Task GetStationsAsync_ReturnsCachedStationsUntilCacheExpires()
    {
        var requestCount = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            requestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CreateStationsResponse("Central Station", 8), Encoding.UTF8, "application/json")
            });
        });

        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 6, 10, 0, 0, TimeSpan.Zero));
        var service = CreateService(handler, timeProvider);

        var first = await service.GetStationsAsync(TestContext.Current.CancellationToken);
        timeProvider.SetUtcNow(timeProvider.GetUtcNow().AddMinutes(1));
        var second = await service.GetStationsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, requestCount);
        Assert.Same(first, second);
        Assert.Equal("Central Station", Assert.Single(second).Name);
    }

    [Fact]
    public async Task GetStationsAsync_RefreshesStationsAfterCacheExpires()
    {
        var responses = new Queue<string>([
            CreateStationsResponse("Central Station", 8),
            CreateStationsResponse("Updated Station", 6)
        ]);

        var requestCount = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            requestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses.Dequeue(), Encoding.UTF8, "application/json")
            });
        });

        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 6, 10, 0, 0, TimeSpan.Zero));
        var service = CreateService(handler, timeProvider);

        _ = await service.GetStationsAsync(TestContext.Current.CancellationToken);
        timeProvider.SetUtcNow(timeProvider.GetUtcNow().AddMinutes(2).AddSeconds(1));
        var refreshed = await service.GetStationsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, requestCount);
        var station = Assert.Single(refreshed);
        Assert.Equal("Updated Station", station.Name);
        Assert.Equal(6, station.BikesAvailable);
    }

    private static LiveStationCacheService CreateService(HttpMessageHandler handler, TimeProvider timeProvider)
    {
        var client = new DigitransitStationClient(
            new HttpClient(handler),
            Options.Create(new PollStationsOptions
            {
                DigitransitSubscriptionKey = "test-subscription-key"
            }));

        return new LiveStationCacheService(client, timeProvider, NullLogger<LiveStationCacheService>.Instance);
    }

    private static string CreateStationsResponse(string stationName, int bikesAvailable)
        => $$"""
           {
             "data": {
               "vehicleRentalStations": [
                 {
                   "stationId": "station-001",
                   "name": "{{stationName}}",
                   "lat": 60.1708,
                   "lon": 24.941,
                   "allowPickup": true,
                   "allowDropoff": true,
                   "capacity": 24,
                   "availableVehicles": {
                     "byType": [
                       { "count": {{bikesAvailable}} }
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
           """;

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request);
    }

    private sealed class MutableTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => currentTime;

        public void SetUtcNow(DateTimeOffset newCurrentTime)
            => currentTime = newCurrentTime;
    }
}
