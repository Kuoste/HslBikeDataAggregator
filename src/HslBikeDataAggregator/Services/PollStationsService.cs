using HslBikeDataAggregator.Configuration;
using HslBikeDataAggregator.Models;
using HslBikeDataAggregator.Storage;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HslBikeDataAggregator.Services;

public sealed class PollStationsService(
    DigitransitStationClient digitransitStationClient,
    IBikeDataBlobStorage bikeDataBlobStorage,
    IOptions<PollStationsOptions> options,
    TimeProvider timeProvider,
    ILogger<PollStationsService> logger) : IPollStationsService
{
    public async Task<PollStationsResult> PollAsync(CancellationToken cancellationToken)
    {
        var stations = await digitransitStationClient.FetchStationsAsync(cancellationToken);

        if (stations.Count == 0)
        {
            logger.LogWarning("Digitransit API returned no stations; skipping snapshot write to avoid corrupting the time series.");
            return new PollStationsResult(timeProvider.GetUtcNow(), 0, 0);
        }

        var timestamp = timeProvider.GetUtcNow();
        var snapshotHistoryLimit = Math.Max(1, options.Value.SnapshotHistoryLimit);
        var existingTimeSeries = await bikeDataBlobStorage.GetSnapshotTimeSeriesAsync(cancellationToken) ?? SnapshotTimeSeries.Empty;
        var updatedTimeSeries = CreateUpdatedTimeSeries(existingTimeSeries, stations, timestamp, snapshotHistoryLimit);

        await bikeDataBlobStorage.WriteSnapshotTimeSeriesAsync(updatedTimeSeries, cancellationToken);

        logger.LogInformation(
            "Processed {StationCount} stations and stored {SnapshotCount} snapshots at {Timestamp}.",
            stations.Count,
            updatedTimeSeries.Timestamps.Count,
            timestamp);

        return new PollStationsResult(timestamp, stations.Count, updatedTimeSeries.Timestamps.Count);
    }

    private static SnapshotTimeSeries CreateUpdatedTimeSeries(
        SnapshotTimeSeries existingTimeSeries,
        IReadOnlyList<BikeStation> stations,
        DateTimeOffset timestamp,
        int snapshotHistoryLimit)
    {
        var timestamps = existingTimeSeries.Timestamps
            .Append(timestamp)
            .OrderBy(static value => value)
            .TakeLast(snapshotHistoryLimit)
            .ToArray();

        var existingStationCounts = existingTimeSeries.Stations
            .ToDictionary(
                static station => station.StationId,
                static station => station.Counts,
                StringComparer.Ordinal);

        var existingTimestampCount = existingTimeSeries.Timestamps.Count;

        var stationSeries = stations
            .OrderBy(static station => station.Id, StringComparer.Ordinal)
            .Select(station => new StationCountSeries
            {
                StationId = station.Id,
                Counts = existingStationCounts.TryGetValue(station.Id, out var existingCounts)
                    ? existingCounts.Append(station.BikesAvailable).TakeLast(snapshotHistoryLimit).ToArray()
                    // Station has no history (new station, or prior empty-API run cleared all rows).
                    // Backfill with -1 ("no data") so the count series length matches the timestamp
                    // series length, which is required by the serialiser. The frontend treats -1 as
                    // a missing observation rather than a genuine zero-bike reading.
                    : Enumerable.Repeat(-1, existingTimestampCount).Append(station.BikesAvailable).TakeLast(snapshotHistoryLimit).ToArray()
            })
            .ToArray();

        return new SnapshotTimeSeries
        {
            IntervalMinutes = ComputeIntervalMinutes(timestamps, existingTimeSeries.IntervalMinutes),
            Timestamps = timestamps,
            Stations = stationSeries
        };
    }

    private static int ComputeIntervalMinutes(IReadOnlyList<DateTimeOffset> timestamps, int fallbackIntervalMinutes)
    {
        for (var index = timestamps.Count - 1; index > 0; index--)
        {
            var delta = timestamps[index] - timestamps[index - 1];
            if (delta > TimeSpan.Zero)
            {
                return Math.Max(1, (int)Math.Round(delta.TotalMinutes, MidpointRounding.AwayFromZero));
            }
        }

        return fallbackIntervalMinutes;
    }
}
