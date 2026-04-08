using System.Net;

using HslBikeDataAggregator.Models;
using HslBikeDataAggregator.Services;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace HslBikeDataAggregator.Functions;

public sealed class StationsFunctions(
    AggregatedBikeDataService bikeDataService,
    ILogger<StationsFunctions> logger)
{
    private const string LiveStationsCacheControl = "public, max-age=30";
    private const string SnapshotsCacheControl = "public, max-age=900";
    private const string StationStatisticsCacheControl = "public, max-age=3600";

    [Function(nameof(GetStations))]
    public Task<HttpResponseData> GetStations(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "stations")] HttpRequestData request,
        CancellationToken cancellationToken)
        => CreateJsonResponseAsync(request, bikeDataService.GetStationsAsync(cancellationToken), LiveStationsCacheControl, cancellationToken);

    [Function(nameof(GetSnapshots))]
    public Task<HttpResponseData> GetSnapshots(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "snapshots")] HttpRequestData request,
        CancellationToken cancellationToken)
        => CreateJsonResponseAsync(request, bikeDataService.GetSnapshotsAsync(cancellationToken), SnapshotsCacheControl, cancellationToken);

    [Function(nameof(GetStationAvailability))]
    public Task<HttpResponseData> GetStationAvailability(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "stations/{stationId}/availability")] HttpRequestData request,
        string stationId,
        CancellationToken cancellationToken)
        => CreateJsonResponseAsync(request, bikeDataService.GetAvailabilityAsync(stationId, cancellationToken), StationStatisticsCacheControl, cancellationToken);

    [Function(nameof(GetStationDestinations))]
    public Task<HttpResponseData> GetStationDestinations(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "stations/{stationId}/destinations")] HttpRequestData request,
        string stationId,
        CancellationToken cancellationToken)
        => CreateJsonResponseAsync(request, bikeDataService.GetDestinationsAsync(stationId, cancellationToken), StationStatisticsCacheControl, cancellationToken);

    private async Task<HttpResponseData> CreateJsonResponseAsync<T>(
        HttpRequestData request,
        Task<IReadOnlyList<T>> payloadTask,
        string cacheControl,
        CancellationToken cancellationToken)
    {
        var payload = await payloadTask;
        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Cache-Control", cacheControl);
        await response.WriteAsJsonAsync(payload, cancellationToken);
        logger.LogInformation("Served {PayloadType} response with {Count} items.", typeof(T).Name, payload.Count);
        return response;
    }
}
