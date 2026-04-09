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

        var stationSeries = stations
            .OrderBy(static station => station.Id, StringComparer.Ordinal)
            .Select(station => new StationCountSeries
            {
                StationId = station.Id,
                Counts = existingStationCounts.TryGetValue(station.Id, out var existingCounts)
                    ? existingCounts.Append(station.BikesAvailable).TakeLast(snapshotHistoryLimit).ToArray()
                    : [station.BikesAvailable]
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
