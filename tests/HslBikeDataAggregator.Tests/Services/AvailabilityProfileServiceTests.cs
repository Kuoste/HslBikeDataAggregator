using HslBikeDataAggregator.Models;
using HslBikeDataAggregator.Services;

namespace HslBikeDataAggregator.Tests.Services;

public sealed class AvailabilityProfileServiceTests
{
    [Fact]
    public void BuildProfiles_GroupsSnapshotsByStationAndHour()
    {
        var snapshots = new[]
        {
            new StationSnapshot
            {
                Timestamp = new DateTimeOffset(2026, 4, 4, 10, 5, 0, TimeSpan.Zero),
                BikeCounts = new Dictionary<string, int>
                {
                    ["station-001"] = 7,
                    ["station-002"] = 3
                }
            },
            new StationSnapshot
            {
                Timestamp = new DateTimeOffset(2026, 4, 4, 10, 35, 0, TimeSpan.Zero),
                BikeCounts = new Dictionary<string, int>
                {
                    ["station-001"] = 9
                }
            },
            new StationSnapshot
            {
                Timestamp = new DateTimeOffset(2026, 4, 4, 11, 5, 0, TimeSpan.Zero),
                BikeCounts = new Dictionary<string, int>
                {
                    ["station-001"] = 5,
                    ["station-002"] = 4
                }
            }
        };

        var service = new AvailabilityProfileService();

        var profiles = service.BuildProfiles(snapshots);

        var station001Profile = Assert.IsAssignableFrom<IReadOnlyList<HourlyAvailability>>(profiles["station-001"]);
        Assert.Collection(
            station001Profile,
            availability =>
            {
                Assert.Equal(10, availability.Hour);
                Assert.Equal(8, availability.AverageBikesAvailable);
            },
            availability =>
            {
                Assert.Equal(11, availability.Hour);
                Assert.Equal(5, availability.AverageBikesAvailable);
            });

        var station002Profile = Assert.IsAssignableFrom<IReadOnlyList<HourlyAvailability>>(profiles["station-002"]);
        Assert.Collection(
            station002Profile,
            availability =>
            {
                Assert.Equal(10, availability.Hour);
                Assert.Equal(3, availability.AverageBikesAvailable);
            },
            availability =>
            {
                Assert.Equal(11, availability.Hour);
                Assert.Equal(4, availability.AverageBikesAvailable);
            });
    }
}
