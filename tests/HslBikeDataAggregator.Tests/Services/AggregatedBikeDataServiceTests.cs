using HslBikeDataAggregator.Services;

namespace HslBikeDataAggregator.Tests.Services;

public sealed class AggregatedBikeDataServiceTests
{
    private static readonly CancellationToken CancellationToken = CancellationToken.None;

    [Fact]
    public async Task GetStationsAsync_ReturnsEmptyCollection()
    {
        var service = new AggregatedBikeDataService();

        var result = await service.GetStationsAsync(CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSnapshotsAsync_ReturnsEmptyCollection()
    {
        var service = new AggregatedBikeDataService();

        var result = await service.GetSnapshotsAsync(CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAvailabilityAsync_ReturnsEmptyCollection()
    {
        var service = new AggregatedBikeDataService();

        var result = await service.GetAvailabilityAsync("station-001", CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDestinationsAsync_ReturnsEmptyCollection()
    {
        var service = new AggregatedBikeDataService();

        var result = await service.GetDestinationsAsync("station-001", CancellationToken);

        Assert.Empty(result);
    }
}
