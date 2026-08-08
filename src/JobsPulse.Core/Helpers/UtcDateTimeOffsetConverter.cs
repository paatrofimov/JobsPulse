using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobsPulse.Core.Helpers;

public sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.GetDateTimeOffset().ToUniversalTime();
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTimeOffset value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToUniversalTime());
    }
}