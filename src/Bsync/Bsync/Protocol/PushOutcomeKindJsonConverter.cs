using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bsync.Protocol;

/// <summary>Encodes <see cref="PushOutcomeKind"/> as its wire name; integers and unknown names are rejected.</summary>
public sealed class PushOutcomeKindJsonConverter : JsonStringEnumConverter<PushOutcomeKind>
{
    /// <summary>Creates the converter.</summary>
    public PushOutcomeKindJsonConverter()
        : base(namingPolicy: null, allowIntegerValues: false)
    {
    }
}
