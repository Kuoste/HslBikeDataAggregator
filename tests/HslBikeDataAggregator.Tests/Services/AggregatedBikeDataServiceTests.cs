using HslBikeDataAggregator.Models;
using HslBikeDataAggregator.Services;
using HslBikeDataAggregator.Storage;

using Moq;

namespace HslBikeDataAggregator.Tests.Services;

public sealed class AggregatedBikeDataServiceTests
{
    private static readonly CancellationToken CancellationToken = CancellationToken.None;

    [Fact]
    public async Task GetStationsAsync_ReturnsStationsFromBlobStorage()
    {
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetLatestStationsAsync(CancellationToken))
            .ReturnsAsync([
                new BikeStation
                {
                    Id = "station-001",
                    Name = "Central Station",
                    Lat = 60.1708,
                    Lon = 24.941,
                    Capacity = 24,
                    BikesAvailable = 8,
                    SpacesAvailable = 16,
                    IsActive = true
                }
            ]);

        var service = new AggregatedBikeDataService(blobStorage.Object);

        var result = await service.GetStationsAsync(CancellationToken);

        var station = Assert.Single(result);
        Assert.Equal("station-001", station.Id);
    }

    [Fact]
    public async Task GetStationsAsync_ReturnsEmptyCollectionWhenBlobStorageHasNoStations()
    {
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetLatestStationsAsync(CancellationToken))
            .ReturnsAsync([]);

        var service = new AggregatedBikeDataService(blobStorage.Object);

        var result = await service.GetStationsAsync(CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSnapshotsAsync_ReturnsSnapshotsFromBlobStorage()
    {
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetRecentSnapshotsAsync(CancellationToken))
            .ReturnsAsync([
                new StationSnapshot
                {
                    Timestamp = new DateTimeOffset(2026, 4, 4, 9, 45, 0, TimeSpan.Zero),
                    BikeCounts = new Dictionary<string, int>
                    {
                        ["station-001"] = 8
                    }
                }
            ]);

        var service = new AggregatedBikeDataService(blobStorage.Object);

        var result = await service.GetSnapshotsAsync(CancellationToken);

        var snapshot = Assert.Single(result);
        Assert.Equal(8, snapshot.BikeCounts["station-001"]);
    }

    [Fact]
    public async Task GetSnapshotsAsync_ReturnsEmptyCollectionWhenBlobStorageHasNoSnapshots()
    {
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetRecentSnapshotsAsync(CancellationToken))
            .ReturnsAsync([]);

        var service = new AggregatedBikeDataService(blobStorage.Object);

        var result = await service.GetSnapshotsAsync(CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAvailabilityAsync_ReturnsAvailabilityProfileFromBlobStorage()
    {
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetAvailabilityProfileAsync("station-001", CancellationToken))
            .ReturnsAsync([
                new HourlyAvailability
                {
                    Hour = 10,
                    AverageBikesAvailable = 7.5
                }
            ]);

        var service = new AggregatedBikeDataService(blobStorage.Object);

        var result = await service.GetAvailabilityAsync("station-001", CancellationToken);

        var availability = Assert.Single(result);
        Assert.Equal(10, availability.Hour);
        Assert.Equal(7.5, availability.AverageBikesAvailable);
    }

    [Fact]
    public async Task GetDestinationsAsync_ReturnsDestinationsFromBlobStorage()
    {
        var blobStorage = new Mock<IBikeDataBlobStorage>();
        blobStorage
            .Setup(storage => storage.GetStationDestinationsAsync("station-001", CancellationToken))
            .ReturnsAsync([
                new StationHistory
                {
                    DepartureStationId = "station-001",
                    ArrivalStationId = "station-002",
                    TripCount = 12,
                    AverageDurationSeconds = 425.5,
                    AverageDistanceMetres = 1_280.2
                }
            ]);

        var service = new AggregatedBikeDataService(blobStorage.Object);

        var result = await service.GetDestinationsAsync("station-001", CancellationToken);

        var destination = Assert.Single(result);
        Assert.Equal("station-002", destination.ArrivalStationId);
    }
}
