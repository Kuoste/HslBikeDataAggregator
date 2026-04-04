using HslBikeDataAggregator.Services;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HslBikeDataAggregator.Functions;

public sealed class ProcessStationHistoryFunction(
    ProcessStationHistoryService processStationHistoryService,
    ILogger<ProcessStationHistoryFunction> logger)
{
    /// <summary>
    /// Aggregates configured HSL trip history CSV files into per-station destination profiles.
    /// </summary>
    [Function(nameof(ProcessStationHistory))]
    public async Task ProcessStationHistory([TimerTrigger("%HistoryProcessingCron%")] TimerInfo timerInfo, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "ProcessStationHistory trigger fired at {Timestamp}. Next schedule: {NextSchedule}.",
            DateTimeOffset.UtcNow,
            timerInfo.ScheduleStatus?.Next);

        var result = await processStationHistoryService.ProcessAsync(cancellationToken);

        logger.LogInformation(
            "ProcessStationHistory completed. Processed {JourneyCount} journeys from {SourceCount} sources into {StationCount} destination profiles.",
            result.JourneyCount,
            result.SourceCount,
            result.StationCount);
    }
}
