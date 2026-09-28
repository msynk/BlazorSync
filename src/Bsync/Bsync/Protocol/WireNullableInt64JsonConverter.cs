using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bsync.Protocol;

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
