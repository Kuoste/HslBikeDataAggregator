using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HslBikeDataAggregator.Models;

[JsonConverter(typeof(SnapshotTimeSeriesJsonConverter))]
public sealed record SnapshotTimeSeries
{
    public static SnapshotTimeSeries Empty { get; } = new()
    {
        IntervalMinutes = 0,
        Timestamps = [],
        Stations = []
    };

    public required int IntervalMinutes { get; init; }

    public required IReadOnlyList<DateTimeOffset> Timestamps { get; init; }

    public required IReadOnlyList<StationCountSeries> Stations { get; init; }

    private sealed class SnapshotTimeSeriesJsonConverter : JsonConverter<SnapshotTimeSeries>
    {
        public override SnapshotTimeSeries? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected snapshot time series object.");
            }

            var intervalMinutes = 0;
            var timestamps = new List<DateTimeOffset>();
            var stations = new List<StationCountSeries>();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new SnapshotTimeSeries
                    {
                        IntervalMinutes = intervalMinutes,
                        Timestamps = timestamps,
                        Stations = stations
                    };
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected snapshot time series property.");
                }

                var propertyName = reader.GetString();
                reader.Read();

                switch (propertyName)
                {
                    case "intervalMinutes":
                        intervalMinutes = reader.GetInt32();
                        break;
                    case "timestamps":
                        timestamps = ReadTimestamps(ref reader);
                        break;
                    case "rows":
                        stations = ReadStations(ref reader);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            throw new JsonException("Incomplete snapshot time series payload.");
        }

        public override void Write(Utf8JsonWriter writer, SnapshotTimeSeries value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);

            writer.WriteStartObject();
            writer.WriteNumber("intervalMinutes", value.IntervalMinutes);

            writer.WritePropertyName("timestamps");
            writer.WriteStartArray();
            foreach (var timestamp in value.Timestamps)
            {
                writer.WriteStringValue(timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture));
            }

            writer.WriteEndArray();

            writer.WritePropertyName("rows");
            writer.WriteStartArray();
            foreach (var station in value.Stations)
            {
                if (station.Counts.Count != value.Timestamps.Count)
                {
                    throw new JsonException($"Station '{station.StationId}' count series length {station.Counts.Count} did not match timestamp count {value.Timestamps.Count}.");
                }

                writer.WriteStartArray();
                writer.WriteStringValue(station.StationId);
                foreach (var count in station.Counts)
                {
                    writer.WriteNumberValue(count);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static List<DateTimeOffset> ReadTimestamps(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("Expected snapshot timestamp array.");
            }

            var timestamps = new List<DateTimeOffset>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return timestamps;
                }

                if (reader.TokenType != JsonTokenType.String)
                {
                    throw new JsonException("Expected snapshot timestamp string.");
                }

                var timestamp = reader.GetString();
                if (!DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedTimestamp))
                {
                    throw new JsonException($"Invalid snapshot timestamp '{timestamp}'.");
                }

                timestamps.Add(parsedTimestamp);
            }

            throw new JsonException("Incomplete snapshot timestamp array.");
        }

        private static List<StationCountSeries> ReadStations(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("Expected snapshot station rows array.");
            }

            var stations = new List<StationCountSeries>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return stations;
                }

                stations.Add(ReadStation(ref reader));
            }

            throw new JsonException("Incomplete snapshot station rows array.");
        }

        private static StationCountSeries ReadStation(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("Expected snapshot station row array.");
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Expected snapshot station id.");
            }

            var stationId = reader.GetString();
            if (string.IsNullOrWhiteSpace(stationId))
            {
                throw new JsonException("Snapshot station id cannot be empty.");
            }

            var counts = new List<int>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return new StationCountSeries
                    {
                        StationId = stationId,
                        Counts = counts
                    };
                }

                if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var count))
                {
                    throw new JsonException("Expected snapshot station count integer.");
                }

                counts.Add(count);
            }

            throw new JsonException("Incomplete snapshot station row.");
        }
    }
}
