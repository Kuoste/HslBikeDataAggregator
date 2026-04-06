using Azure.Storage.Blobs;

using HslBikeDataAggregator.Configuration;
using HslBikeDataAggregator.Services;
using HslBikeDataAggregator.Storage;

using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddOptions<PollStationsOptions>()
    .Configure<IConfiguration>((options, configuration) =>
    {
        options.DigitransitSubscriptionKey = configuration["DigitransitSubscriptionKey"] ?? string.Empty;
        options.SnapshotHistoryLimit = configuration.GetValue<int?>("SnapshotHistoryLimit") ?? 60;
    });

builder.Services
    .AddOptions<HistoryProcessingOptions>()
    .Configure<IConfiguration>((options, configuration) =>
    {
        options.TripHistoryUrlPattern = configuration["HistoryProcessing:TripHistoryUrlPattern"] ?? HistoryProcessingOptions.DefaultTripHistoryUrlPattern;
        options.RollingWindowMonthCount = Math.Max(
            configuration.GetValue<int?>("HistoryProcessing:RollingWindowMonthCount") ?? HistoryProcessingOptions.DefaultRollingWindowMonthCount,
            1);
        options.AvailabilityProbeMonthCount = Math.Max(
            configuration.GetValue<int?>("HistoryProcessing:AvailabilityProbeMonthCount") ?? HistoryProcessingOptions.DefaultAvailabilityProbeMonthCount,
            options.RollingWindowMonthCount);
    });

builder.Services.AddHttpClient<DigitransitStationClient>();
builder.Services.AddHttpClient<ProcessStationHistoryService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AvailabilityProfileService>();
builder.Services.AddSingleton<LiveStationCacheService>();
builder.Services.AddSingleton(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var connectionString = configuration["AzureWebJobsStorage"];
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("AzureWebJobsStorage setting is required.");
    }

    return new BlobContainerClient(connectionString, BikeDataBlobNames.ContainerName);
});
builder.Services.AddSingleton<IBikeDataBlobStorage, BikeDataBlobStorage>();
builder.Services.AddSingleton<IPollStationsService, PollStationsService>();
builder.Services.AddSingleton<AggregatedBikeDataService>();

builder.Build().Run();
