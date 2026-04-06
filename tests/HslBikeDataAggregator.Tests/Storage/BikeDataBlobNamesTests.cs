using HslBikeDataAggregator.Storage;

namespace HslBikeDataAggregator.Tests.Storage;

public sealed class BikeDataBlobNamesTests
{
    [Theory]
    [InlineData("001", "availability/001.json")]
    [InlineData("HSL-042", "availability/HSL-042.json")]
    [InlineData("station_99", "availability/station_99.json")]
    public void AvailabilityProfile_ValidStationId_ReturnsSafeBlobName(string stationId, string expected)
    {
        var result = BikeDataBlobNames.AvailabilityProfile(stationId);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("001", "destinations/001.json")]
    [InlineData("HSL-042", "destinations/HSL-042.json")]
    [InlineData("station_99", "destinations/station_99.json")]
    public void DestinationProfile_ValidStationId_ReturnsSafeBlobName(string stationId, string expected)
    {
        var result = BikeDataBlobNames.DestinationProfile(stationId);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("station/../secret")]
    [InlineData("station/id")]
    [InlineData("station%00id")]
    [InlineData("..")]
    public void AvailabilityProfile_PathTraversalAttempt_ThrowsArgumentException(string maliciousId)
    {
        var exception = Assert.Throws<ArgumentException>(() => BikeDataBlobNames.AvailabilityProfile(maliciousId));

        Assert.Equal("stationId", exception.ParamName);
    }

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("station/../secret")]
    [InlineData("station/id")]
    public void DestinationProfile_PathTraversalAttempt_ThrowsArgumentException(string maliciousId)
    {
        var exception = Assert.Throws<ArgumentException>(() => BikeDataBlobNames.DestinationProfile(maliciousId));

        Assert.Equal("stationId", exception.ParamName);
    }

    [Fact]
    public void AvailabilityProfile_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => BikeDataBlobNames.AvailabilityProfile(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AvailabilityProfile_EmptyOrWhitespace_ThrowsArgumentException(string stationId)
    {
        Assert.Throws<ArgumentException>(() => BikeDataBlobNames.AvailabilityProfile(stationId));
    }

    [Fact]
    public void DestinationProfile_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => BikeDataBlobNames.DestinationProfile(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DestinationProfile_EmptyOrWhitespace_ThrowsArgumentException(string stationId)
    {
        Assert.Throws<ArgumentException>(() => BikeDataBlobNames.DestinationProfile(stationId));
    }
}
