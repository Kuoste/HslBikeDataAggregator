using System.Globalization;

using HslBikeDataAggregator.Configuration;
using HslBikeDataAggregator.Models;
using HslBikeDataAggregator.Storage;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HslBikeDataAggregator.Services;

public sealed class ProcessStationHistoryService(
    HttpClient httpClient,
    IOptions<HistoryProcessingOptions> options,
    IBikeDataBlobStorage bikeDataBlobStorage,
    ILogger<ProcessStationHistoryService> logger)
{
    private readonly string[] tripHistoryUrls = options.Value.TripHistoryUrls
        .Where(static url => !string.IsNullOrWhiteSpace(url))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>
    /// Downloads configured trip history CSV sources, aggregates per-station destinations, and stores the profiles.
    /// </summary>
    public async Task<HistoryProcessingResult> ProcessAsync(CancellationToken cancellationToken)
    {
        if (tripHistoryUrls.Length == 0)
        {
            logger.LogWarning("No trip history URLs are configured. Skipping destination processing.");
            return new HistoryProcessingResult
            {
                SourceCount = 0,
                JourneyCount = 0,
                StationCount = 0
            };
        }

        var aggregates = new Dictionary<(string DepartureStationId, string ArrivalStationId), DestinationAggregate>();
        var processedJourneyCount = 0;

        foreach (var tripHistoryUrl in tripHistoryUrls)
        {
            processedJourneyCount += await AggregateSourceAsync(tripHistoryUrl, aggregates, cancellationToken);
        }

        var stationDestinations = new Dictionary<string, IReadOnlyList<StationHistory>>(StringComparer.Ordinal);
        foreach (var stationGroup in aggregates.GroupBy(static aggregate => aggregate.Key.DepartureStationId, StringComparer.Ordinal))
        {
            stationDestinations[stationGroup.Key] = stationGroup
                .Select(static aggregate => aggregate.Value.ToStationHistory(aggregate.Key.DepartureStationId, aggregate.Key.ArrivalStationId))
                .OrderByDescending(static history => history.TripCount)
                .ThenBy(static history => history.ArrivalStationId, StringComparer.Ordinal)
                .ToArray();
        }

        foreach (var stationDestination in stationDestinations.OrderBy(static stationDestination => stationDestination.Key, StringComparer.Ordinal))
        {
            await bikeDataBlobStorage.WriteStationDestinationsAsync(stationDestination.Key, stationDestination.Value, cancellationToken);
        }

        logger.LogInformation(
            "Processed {JourneyCount} historical journeys from {SourceCount} sources into {StationCount} destination profiles.",
            processedJourneyCount,
            tripHistoryUrls.Length,
            stationDestinations.Count);

        return new HistoryProcessingResult
        {
            SourceCount = tripHistoryUrls.Length,
            JourneyCount = processedJourneyCount,
            StationCount = stationDestinations.Count
        };
    }

    private async Task<int> AggregateSourceAsync(
        string sourceUrl,
        Dictionary<(string DepartureStationId, string ArrivalStationId), DestinationAggregate> aggregates,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(responseStream);
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            logger.LogWarning("Trip history source {SourceUrl} was empty.", sourceUrl);
            return 0;
        }

        var columnIndexes = GetColumnIndexes(ParseCsvLine(headerLine));
        var processedJourneyCount = 0;

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!TryCreateJourney(ParseCsvLine(line), columnIndexes, out var journey))
            {
                continue;
            }

            var key = (journey.DepartureStationId, journey.ArrivalStationId);
            if (!aggregates.TryGetValue(key, out var aggregate))
            {
                aggregate = new DestinationAggregate();
                aggregates[key] = aggregate;
            }

            aggregate.Add(journey.DurationSeconds, journey.DistanceMetres);
            processedJourneyCount++;
        }

        return processedJourneyCount;
    }

    private static TripHistoryColumnIndexes GetColumnIndexes(IReadOnlyList<string> headers)
    {
        var normalisedHeaders = headers
            .Select(NormaliseHeader)
            .ToArray();

        return new TripHistoryColumnIndexes(
            GetRequiredColumnIndex(normalisedHeaders, "departure station id"),
            GetRequiredColumnIndex(normalisedHeaders, "return station id"),
            GetRequiredColumnIndex(normalisedHeaders, "covered distance (m)"),
            GetRequiredColumnIndex(normalisedHeaders, "duration (sec.)"));
    }

    private static int GetRequiredColumnIndex(IReadOnlyList<string> headers, string requiredHeader)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            if (string.Equals(headers[index], requiredHeader, StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new InvalidOperationException($"Trip history CSV is missing required column '{requiredHeader}'.");
    }

    private static bool TryCreateJourney(
        IReadOnlyList<string> fields,
        TripHistoryColumnIndexes columnIndexes,
        out TripHistoryJourney journey)
    {
        journey = default;
        if (fields.Count <= columnIndexes.DurationSeconds
            || fields.Count <= columnIndexes.DistanceMetres
            || fields.Count <= columnIndexes.ArrivalStationId
            || fields.Count <= columnIndexes.DepartureStationId)
        {
            return false;
        }

        var departureStationId = fields[columnIndexes.DepartureStationId].Trim();
        var arrivalStationId = fields[columnIndexes.ArrivalStationId].Trim();
        if (string.IsNullOrWhiteSpace(departureStationId) || string.IsNullOrWhiteSpace(arrivalStationId))
        {
            return false;
        }

        if (!double.TryParse(fields[columnIndexes.DurationSeconds], CultureInfo.InvariantCulture, out var durationSeconds)
            || !double.TryParse(fields[columnIndexes.DistanceMetres], CultureInfo.InvariantCulture, out var distanceMetres))
        {
            return false;
        }

        journey = new TripHistoryJourney(departureStationId, arrivalStationId, durationSeconds, distanceMetres);
        return true;
    }

    private static string NormaliseHeader(string header)
        => header.Trim().Trim('\uFEFF').Trim('"').Trim().ToLowerInvariant();

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var currentField = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var character in line)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (character == ',' && !inQuotes)
            {
                fields.Add(currentField.ToString());
                currentField.Clear();
                continue;
            }

            currentField.Append(character);
        }

        fields.Add(currentField.ToString());
        return fields;
    }

    private sealed class DestinationAggregate
    {
        public int TripCount { get; private set; }

        public double TotalDurationSeconds { get; private set; }

        public double TotalDistanceMetres { get; private set; }

        public void Add(double durationSeconds, double distanceMetres)
        {
            TripCount++;
            TotalDurationSeconds += durationSeconds;
            TotalDistanceMetres += distanceMetres;
        }

        public StationHistory ToStationHistory(string departureStationId, string arrivalStationId)
            => new()
            {
                DepartureStationId = departureStationId,
                ArrivalStationId = arrivalStationId,
                TripCount = TripCount,
                AverageDurationSeconds = TotalDurationSeconds / TripCount,
                AverageDistanceMetres = TotalDistanceMetres / TripCount
            };
    }

    private readonly record struct TripHistoryColumnIndexes(
        int DepartureStationId,
        int ArrivalStationId,
        int DistanceMetres,
        int DurationSeconds);

    private readonly record struct TripHistoryJourney(
        string DepartureStationId,
        string ArrivalStationId,
        double DurationSeconds,
        double DistanceMetres);
}
