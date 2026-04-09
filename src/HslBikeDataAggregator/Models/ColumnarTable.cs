using System.Text.Json.Serialization;

namespace HslBikeDataAggregator.Models;

public sealed record ColumnarTable
{
    public static ColumnarTable Empty { get; } = new()
    {
        Fields = [],
        Rows = []
    };

    [JsonPropertyName("fields")]
    public required string[] Fields { get; init; }

    [JsonPropertyName("rows")]
    public required object[][] Rows { get; init; }
}
