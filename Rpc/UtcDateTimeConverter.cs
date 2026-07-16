using System.Globalization;
using System.Text.Json.Serialization;

namespace H3xBoardServer.Rpc;

/// <summary>
/// All DateTime values in this app originate from DateTime.UtcNow, but SQLite has no native
/// datetime type — linq2db round-trips them through a string column, and reading them back
/// yields DateTimeKind.Unspecified. Left alone, System.Text.Json then serializes them without a
/// "Z"/offset suffix, so clients parse them as local time instead of UTC. Force UTC on write
/// regardless of the value's Kind.
/// </summary>
public class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
