using System.Globalization;
using System.Net;

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
    TimeProvider timeProvider,
    ILogger<ProcessStationHistoryService> logger)
{
    private const string CanonicalStationIdPrefix = "smoove:";
    private readonly string tripHistoryUrlPattern = string.IsNullOrWhiteSpace(options.Value.TripHistoryUrlPattern)
        ? HistoryProcessingOptions.DefaultTripHistoryUrlPattern
        : options.Value.TripHistoryUrlPattern;
    private readonly string allowedTripHistoryHost = string.IsNullOrWhiteSpace(options.Value.AllowedTripHistoryHost)
        ? HistoryProcessingOptions.DefaultAllowedTripHistoryHost
        : options.Value.AllowedTripHistoryHost;
    private readonly int availabilityProbeMonthCount = Math.Max(options.Value.AvailabilityProbeMonthCount, 1);

    /// <summary>
    /// Discovers the newest available HSL trip history CSV, aggregates monthly station statistics, and stores them.
    /// </summary>
    public async Task<HistoryProcessingResult> ProcessAsync(CancellationToken cancellationToken)
    {
        var availableMonth = await DiscoverAvailableMonthAsync(cancellationToken);
        if (availableMonth is null)
        {
            logger.LogWarning("No recent HSL trip history CSV files were available. Skipping monthly statistics processing.");
            return new HistoryProcessingResult
            {
                SourceCount = 0,
                JourneyCount = 0,
                StationCount = 0
            };
        }

        var month = availableMonth.Value;
        var monthKey = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var stationAggregates = new Dictionary<string, StationStatisticsAggregate>(StringComparer.Ordinal);
        var processedJourneyCount = await AggregateSourceAsync(BuildTripHistoryUrl(month), stationAggregates, cancellationToken);

        var stationStatistics = stationAggregates
            .OrderBy(static aggregate => aggregate.Key, StringComparer.Ordinal)
            .ToDictionary(
                aggregate => aggregate.Key,
                aggregate => aggregate.Value.ToMonthlyStatistics(monthKey),
                StringComparer.Ordinal);

        foreach (var stationStatistic in stationStatistics)
        {
            var existingStatistics = await bikeDataBlobStorage.GetMonthlyStatisticsAsync(stationStatistic.Key, cancellationToken);
            if (string.Equals(existingStatistics?.Month, monthKey, StringComparison.Ordinal))
            {
                continue;
            }

            await bikeDataBlobStorage.WriteMonthlyStatisticsAsync(stationStatistic.Key, stationStatistic.Value, cancellationToken);
        }

        var currentStationIds = stationStatistics.Keys.ToHashSet(StringComparer.Ordinal);
        var existingStationIds = await bikeDataBlobStorage.ListMonthlyStatisticStationIdsAsync(cancellationToken);
        foreach (var staleStationId in existingStationIds.Except(currentStationIds, StringComparer.Ordinal).OrderBy(static stationId => stationId, StringComparer.Ordinal))
        {
            await bikeDataBlobStorage.DeleteMonthlyStatisticsAsync(staleStationId, cancellationToken);
        }

        logger.LogInformation(
            "Processed {JourneyCount} historical journeys from {SourceCount} source into {StationCount} monthly station statistics payloads for month {Month}.",
            processedJourneyCount,
            1,
            stationStatistics.Count,
            monthKey);

        return new HistoryProcessingResult
        {
            SourceCount = 1,
            JourneyCount = processedJourneyCount,
            StationCount = stationStatistics.Count
        };
    }

    private async Task<DateOnly?> DiscoverAvailableMonthAsync(CancellationToken cancellationToken)
    {
        var newestCandidateMonth = new DateOnly(timeProvider.GetUtcNow().Year, timeProvider.GetUtcNow().Month, 1);

        for (var monthOffset = 0; monthOffset < availabilityProbeMonthCount; monthOffset++)
        {
            var candidateMonth = newestCandidateMonth.AddMonths(-monthOffset);
            if (await TripHistoryMonthExistsAsync(candidateMonth, cancellationToken))
            {
                return candidateMonth;
            }
        }

        return null;
    }

    private async Task<bool> TripHistoryMonthExistsAsync(DateOnly month, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(BuildTripHistoryUrl(month), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    private string BuildTripHistoryUrl(DateOnly month)
    {
        var url = string.Format(CultureInfo.InvariantCulture, tripHistoryUrlPattern, month.ToDateTime(TimeOnly.MinValue));

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, allowedTripHistoryHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The resolved trip history URL '{url}' does not target the allowed host (https://{allowedTripHistoryHost}).");
        }

        return url;
    }

    private async Task<int> AggregateSourceAsync(
        string sourceUrl,
        Dictionary<string, StationStatisticsAggregate> stationAggregates,
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

            GetOrCreateStationAggregate(stationAggregates, journey.DepartureStationId)
                .AddDeparture(journey.DepartureTime);
            GetOrCreateStationAggregate(stationAggregates, journey.ArrivalStationId)
                .AddArrival(journey.ArrivalTime);
            GetOrCreateStationAggregate(stationAggregates, journey.DepartureStationId)
                .AddDestination(journey.ArrivalStationId, journey.DurationSeconds, journey.DistanceMetres);
            processedJourneyCount++;
        }

        return processedJourneyCount;
    }

    private static StationStatisticsAggregate GetOrCreateStationAggregate(
        Dictionary<string, StationStatisticsAggregate> stationAggregates,
        string stationId)
    {
        if (!stationAggregates.TryGetValue(stationId, out var aggregate))
        {
            aggregate = new StationStatisticsAggregate();
            stationAggregates[stationId] = aggregate;
        }

        return aggregate;
    }

    private static TripHistoryColumnIndexes GetColumnIndexes(IReadOnlyList<string> headers)
    {
        var normalisedHeaders = headers
            .Select(NormaliseHeader)
            .ToArray();

        return new TripHistoryColumnIndexes(
            GetRequiredColumnIndex(normalisedHeaders, "departure", "departure time"),
            GetRequiredColumnIndex(normalisedHeaders, "return", "return time"),
            GetRequiredColumnIndex(normalisedHeaders, "departure station id"),
            GetRequiredColumnIndex(normalisedHeaders, "return station id"),
            GetRequiredColumnIndex(normalisedHeaders, "covered distance (m)"),
            GetRequiredColumnIndex(normalisedHeaders, "duration (sec.)"));
    }

    private static int GetRequiredColumnIndex(IReadOnlyList<string> headers, params string[] requiredHeaders)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            if (requiredHeaders.Any(requiredHeader => string.Equals(headers[index], requiredHeader, StringComparison.Ordinal)))
            {
                return index;
            }
        }

        throw new InvalidOperationException($"Trip history CSV is missing required column '{string.Join("' or '", requiredHeaders)}'.");
    }

    private static bool TryCreateJourney(
        IReadOnlyList<string> fields,
        TripHistoryColumnIndexes columnIndexes,
        out TripHistoryJourney journey)
    {
        journey = default;
        if (fields.Count <= columnIndexes.ArrivalTime
            || fields.Count <= columnIndexes.DepartureTime
            || fields.Count <= columnIndexes.DurationSeconds
            || fields.Count <= columnIndexes.DistanceMetres
            || fields.Count <= columnIndexes.ArrivalStationId
            || fields.Count <= columnIndexes.DepartureStationId)
        {
            return false;
        }

        var departureStationId = NormaliseStationId(fields[columnIndexes.DepartureStationId]);
        var arrivalStationId = NormaliseStationId(fields[columnIndexes.ArrivalStationId]);
        if (string.IsNullOrWhiteSpace(departureStationId) || string.IsNullOrWhiteSpace(arrivalStationId))
        {
            return false;
        }

        if (!TryParseJourneyTime(fields[columnIndexes.DepartureTime], out var departureTime)
            || !TryParseJourneyTime(fields[columnIndexes.ArrivalTime], out var arrivalTime))
        {
            return false;
        }

        if (!double.TryParse(fields[columnIndexes.DurationSeconds], CultureInfo.InvariantCulture, out var durationSeconds)
            || !double.TryParse(fields[columnIndexes.DistanceMetres], CultureInfo.InvariantCulture, out var distanceMetres))
        {
            return false;
        }

        journey = new TripHistoryJourney(
            departureTime,
            arrivalTime,
            departureStationId,
            arrivalStationId,
            (int)Math.Round(durationSeconds, MidpointRounding.AwayFromZero),
            (int)Math.Round(distanceMetres, MidpointRounding.AwayFromZero));
        return true;
    }

    private static bool TryParseJourneyTime(string value, out DateTime journeyTime)
    {
        if (DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
            out var dateTimeOffset))
        {
            journeyTime = dateTimeOffset.DateTime;
            return true;
        }

        journeyTime = default;
        return false;
    }

    private static string NormaliseStationId(string stationId)
    {
        var trimmedStationId = stationId.Trim();
        if (trimmedStationId.Length == 0)
        {
            return trimmedStationId;
        }

        if (trimmedStationId.StartsWith(CanonicalStationIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = trimmedStationId[CanonicalStationIdPrefix.Length..];
            return suffix.All(char.IsDigit)
                ? string.Concat(CanonicalStationIdPrefix, suffix)
                : trimmedStationId;
        }

        return trimmedStationId.All(char.IsDigit)
            ? string.Concat(CanonicalStationIdPrefix, trimmedStationId)
            : trimmedStationId;
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

    private sealed class StationStatisticsAggregate
    {
        private readonly int[] departuresByHour = new int[24];
        private readonly int[] arrivalsByHour = new int[24];
        private readonly int[] weekdayDeparturesByHour = new int[24];
        private readonly int[] weekendDeparturesByHour = new int[24];
        private readonly int[] weekdayArrivalsByHour = new int[24];
        private readonly int[] weekendArrivalsByHour = new int[24];
        private readonly Dictionary<string, DestinationAggregate> destinations = new(StringComparer.Ordinal);

        public void AddDeparture(DateTime departureTime)
        {
            departuresByHour[departureTime.Hour]++;
            if (IsWeekend(departureTime.DayOfWeek))
            {
                weekendDeparturesByHour[departureTime.Hour]++;
                return;
            }

            weekdayDeparturesByHour[departureTime.Hour]++;
        }

        public void AddArrival(DateTime arrivalTime)
        {
            arrivalsByHour[arrivalTime.Hour]++;
            if (IsWeekend(arrivalTime.DayOfWeek))
            {
                weekendArrivalsByHour[arrivalTime.Hour]++;
                return;
            }

            weekdayArrivalsByHour[arrivalTime.Hour]++;
        }

        public void AddDestination(string arrivalStationId, int durationSeconds, int distanceMetres)
        {
            if (!destinations.TryGetValue(arrivalStationId, out var destinationAggregate))
            {
                destinationAggregate = new DestinationAggregate();
                destinations[arrivalStationId] = destinationAggregate;
            }

            destinationAggregate.Add(durationSeconds, distanceMetres);
        }

        public MonthlyStationStatistics ToMonthlyStatistics(string month)
            => new()
            {
                Month = month,
                Demand = new DemandProfile
                {
                    DeparturesByHour = [.. departuresByHour],
                    ArrivalsByHour = [.. arrivalsByHour],
                    WeekdayDeparturesByHour = [.. weekdayDeparturesByHour],
                    WeekendDeparturesByHour = [.. weekendDeparturesByHour],
                    WeekdayArrivalsByHour = [.. weekdayArrivalsByHour],
                    WeekendArrivalsByHour = [.. weekendArrivalsByHour]
                },
                Destinations = ColumnarTableMapper.ToDestinationTable(
                    destinations
                        .OrderByDescending(static destination => destination.Value.TripCount)
                        .ThenBy(static destination => destination.Key, StringComparer.Ordinal)
                        .Select(destination => destination.Value.ToDestinationRow(destination.Key))
                        .ToArray())
            };

        private static bool IsWeekend(DayOfWeek dayOfWeek)
            => dayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    }

    private sealed class DestinationAggregate
    {
        public int TripCount { get; private set; }

        public long TotalDurationSeconds { get; private set; }

        public long TotalDistanceMetres { get; private set; }

        public void Add(int durationSeconds, int distanceMetres)
        {
            TripCount++;
            TotalDurationSeconds += durationSeconds;
            TotalDistanceMetres += distanceMetres;
        }

        public DestinationRow ToDestinationRow(string arrivalStationId)
            => new()
            {
                ArrivalStationId = arrivalStationId,
                TripCount = TripCount,
                AverageDurationSeconds = (int)Math.Round((double)TotalDurationSeconds / TripCount, MidpointRounding.AwayFromZero),
                AverageDistanceMetres = (int)Math.Round((double)TotalDistanceMetres / TripCount, MidpointRounding.AwayFromZero)
            };
    }

    private readonly record struct TripHistoryColumnIndexes(
        int DepartureTime,
        int ArrivalTime,
        int DepartureStationId,
        int ArrivalStationId,
        int DistanceMetres,
        int DurationSeconds);

    private readonly record struct TripHistoryJourney(
        DateTime DepartureTime,
        DateTime ArrivalTime,
        string DepartureStationId,
        string ArrivalStationId,
        int DurationSeconds,
        int DistanceMetres);
}
