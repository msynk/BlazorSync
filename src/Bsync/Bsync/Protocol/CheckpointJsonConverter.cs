using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bsync.Protocol;

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
