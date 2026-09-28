using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bsync.Protocol;

/// <summary>
/// Encodes a 64-bit integer as a JSON string of ASCII digits (no sign, no leading zeros), so that
/// JavaScript peers cannot silently lose precision above 2^53. Numbers and other formats are rejected.
/// </summary>
public sealed class WireInt64JsonConverter : JsonConverter<long>
{
    /// <inheritdoc />
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ReadDigits(ref reader);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
        WriteDigits(writer, value);

    internal static long ReadDigits(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String || reader.HasValueSequence || reader.ValueIsEscaped)
        {
            throw new JsonException("Expected a 64-bit integer encoded as a string of digits.");
        }

        var span = reader.ValueSpan;
        if (span.Length is 0 or > 19
            || (span.Length > 1 && span[0] == (byte)'0')
            || span.IndexOfAnyExceptInRange((byte)'0', (byte)'9') >= 0
            || !Utf8Parser.TryParse(span, out long value, out var consumed)
            || consumed != span.Length)
        {
            throw new JsonException("Expected a 64-bit integer encoded as a string of digits.");
        }

        return value;
    }

    internal static void WriteDigits(Utf8JsonWriter writer, long value)
    {
        if (value < 0)
        {
            throw new JsonException("Wire integers must not be negative.");
        }

        Span<byte> buffer = stackalloc byte[20];
        Utf8Formatter.TryFormat(value, buffer, out var written);
        writer.WriteStringValue(buffer[..written]);
    }
}
