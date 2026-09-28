using System.Buffers;
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

/// <summary>Encodes a <see cref="Checkpoint"/> as its opaque string, or <c>null</c> for <see cref="Checkpoint.Start"/>.</summary>
public sealed class CheckpointJsonConverter : JsonConverter<Checkpoint>
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override Checkpoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => Checkpoint.Start,
            JsonTokenType.String => new Checkpoint(reader.GetString()),
            _ => throw new JsonException("Expected a checkpoint string or null."),
        };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Checkpoint value, JsonSerializerOptions options)
    {
        if (value.IsStart)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value.Value);
        }
    }
}

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

/// <summary>Nullable form of <see cref="WireInt64JsonConverter"/>; <c>null</c> is written and read as JSON null.</summary>
public sealed class WireNullableInt64JsonConverter : JsonConverter<long?>
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : WireInt64JsonConverter.ReadDigits(ref reader);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value is { } v)
        {
            WireInt64JsonConverter.WriteDigits(writer, v);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

/// <summary>Encodes <see cref="PushOutcomeKind"/> as its wire name; integers and unknown names are rejected.</summary>
public sealed class PushOutcomeKindJsonConverter : JsonStringEnumConverter<PushOutcomeKind>
{
    /// <summary>Creates the converter.</summary>
    public PushOutcomeKindJsonConverter()
        : base(namingPolicy: null, allowIntegerValues: false)
    {
    }
}
