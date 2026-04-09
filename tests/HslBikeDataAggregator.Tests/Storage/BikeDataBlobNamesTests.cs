using HslBikeDataAggregator.Storage;

namespace HslBikeDataAggregator.Tests.Storage;

public sealed class BikeDataBlobNamesTests
{
    [Theory]
    [InlineData("smoove:001", "monthly-stats/smoove:001.json")]
    [InlineData("HSL-042", "monthly-stats/HSL-042.json")]
    [InlineData("station_99", "monthly-stats/station_99.json")]
    [InlineData("smoove:*159", "monthly-stats/smoove:*159.json")]
    [InlineData("smoove:*151", "monthly-stats/smoove:*151.json")]
    [InlineData("station@123", "monthly-stats/station@123.json")]
    [InlineData("station#456", "monthly-stats/station#456.json")]
    [InlineData("station(789)", "monthly-stats/station(789).json")]
    public void MonthlyStatistics_ValidStationId_ReturnsSafeBlobName(string stationId, string expected)
    {
        var result = BikeDataBlobNames.MonthlyStatistics(stationId);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("../../../etc/passwd", "Station ID cannot contain path separators.")]
    [InlineData("station/../secret", "Station ID cannot contain path separators.")]
    [InlineData("station/id", "Station ID cannot contain path separators.")]
    [InlineData("station\\id", "Station ID cannot contain path separators.")]
    [InlineData("station\0id", "Station ID cannot contain control characters.")]
    [InlineData("station\nid", "Station ID cannot contain control characters.")]
    public void MonthlyStatistics_PathTraversalAttempt_ThrowsArgumentException(string maliciousId, string expectedMessage)
    {
        var exception = Assert.Throws<ArgumentException>(() => BikeDataBlobNames.MonthlyStatistics(maliciousId));

        Assert.Equal("stationId", exception.ParamName);
        Assert.StartsWith(expectedMessage, exception.Message);
    }

    [Fact]
    public void MonthlyStatistics_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => BikeDataBlobNames.MonthlyStatistics(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MonthlyStatistics_EmptyOrWhitespace_ThrowsArgumentException(string stationId)
    {
        Assert.Throws<ArgumentException>(() => BikeDataBlobNames.MonthlyStatistics(stationId));
    }

    [Fact]
    public void MonthlyStatistics_StandaloneDotDot_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => BikeDataBlobNames.MonthlyStatistics(".."));

        Assert.Equal("stationId", exception.ParamName);
        Assert.StartsWith("Station ID cannot contain '..'.", exception.Message);
    }
}
