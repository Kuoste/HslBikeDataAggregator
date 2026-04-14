using Azure.Identity;
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

builder.Services.AddHttpClient<DigitransitStationClient>()
    .AddStandardResilienceHandler();
builder.Services.AddHttpClient<ProcessStationHistoryService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LiveStationCacheService>();
builder.Services.AddSingleton(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();

    // Local development: use connection string (e.g. Azurite with UseDevelopmentStorage=true)
    var connectionString = configuration["AzureWebJobsStorage"];
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        return new BlobContainerClient(connectionString, BikeDataBlobNames.ContainerName);
    }

    // Azure: use Managed Identity via AzureWebJobsStorage__accountName
    var accountName = configuration["AzureWebJobsStorage:accountName"];
    if (!string.IsNullOrWhiteSpace(accountName))
    {
        var containerUri = new Uri($"https://{accountName}.blob.core.windows.net/{BikeDataBlobNames.ContainerName}");

        // Use user-assigned managed identity when a client ID is configured
        var clientId = configuration["AzureWebJobsStorage:clientId"];
        var credential = string.IsNullOrWhiteSpace(clientId)
            ? new DefaultAzureCredential()
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = clientId });

        return new BlobContainerClient(containerUri, credential);
    }

    throw new InvalidOperationException("Storage is not configured. Set either AzureWebJobsStorage (local) or AzureWebJobsStorage__accountName (Azure).");
});
builder.Services.AddSingleton<IBikeDataBlobStorage, BikeDataBlobStorage>();
builder.Services.AddSingleton<IPollStationsService, PollStationsService>();
builder.Services.AddSingleton<AggregatedBikeDataService>();

builder.Build().Run();
