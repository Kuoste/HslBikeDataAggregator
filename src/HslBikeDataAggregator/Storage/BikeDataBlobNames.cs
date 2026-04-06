namespace HslBikeDataAggregator.Storage;

public static class BikeDataBlobNames
{
    public const string ContainerName = "bike-data";
    public const string RecentSnapshots = "snapshots/recent.json";

    public static string AvailabilityProfile(string stationId) => $"availability/{stationId}.json";

    public static string DestinationProfile(string stationId) => $"destinations/{stationId}.json";
}
