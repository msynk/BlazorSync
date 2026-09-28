using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bsync.Clocks;

namespace Bsync.Protocol;

/// <summary>
/// Encodes <see cref="HlcTimestamp"/> as its canonical string (<see cref="HlcTimestamp.Encode"/>) and
/// rejects anything else, so the JSON value sorts the same way as the timestamp.
/// </summary>
public sealed class HlcTimestampJsonConverter : JsonConverter<HlcTimestamp>
{
    /// <inheritdoc />
    public override HlcTimestamp Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !HlcTimestamp.TryParse(reader.GetString(), out var value))
        {
            throw new JsonException("Expected a canonical HLC timestamp string.");
        }

        return value;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, HlcTimestamp value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Encode());
}
