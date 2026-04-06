using HslBikeDataAggregator.Models;

using Microsoft.Extensions.Logging;

namespace HslBikeDataAggregator.Services;

public sealed class LiveStationCacheService(
    DigitransitStationClient digitransitStationClient,
    TimeProvider timeProvider,
    ILogger<LiveStationCacheService> logger)
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private CachedStations? cachedStations;

    public async Task<IReadOnlyList<BikeStation>> GetStationsAsync(CancellationToken cancellationToken)
    {
        var currentTime = timeProvider.GetUtcNow();
        if (TryGetCachedStations(currentTime, out var stations))
        {
            logger.LogDebug("Returning {StationCount} cached live stations.", stations.Count);
            return stations;
        }

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            currentTime = timeProvider.GetUtcNow();
            if (TryGetCachedStations(currentTime, out stations))
            {
                logger.LogDebug("Returning {StationCount} cached live stations after waiting for a refresh.", stations.Count);
                return stations;
            }

            stations = await digitransitStationClient.FetchStationsAsync(cancellationToken);
            cachedStations = new CachedStations(stations, currentTime + CacheLifetime);
            logger.LogInformation("Fetched {StationCount} live stations from Digitransit and cached them until {ExpiresAt}.", stations.Count, cachedStations.ExpiresAt);
            return stations;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private bool TryGetCachedStations(DateTimeOffset currentTime, out IReadOnlyList<BikeStation> stations)
    {
        if (cachedStations is not null && cachedStations.ExpiresAt >= currentTime)
        {
            stations = cachedStations.Stations;
            return true;
        }

        stations = [];
        return false;
    }

    private sealed record CachedStations(IReadOnlyList<BikeStation> Stations, DateTimeOffset ExpiresAt);
}
