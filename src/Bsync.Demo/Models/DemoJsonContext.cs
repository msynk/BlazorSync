using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bsync.Demo.Models;

/// <summary>
/// Source-generated JSON metadata for <see cref="DemoNote"/>, so the demo never relies on
/// reflection-based serialization in a trimmed WebAssembly build.
/// </summary>
[JsonSerializable(typeof(DemoNote))]
public sealed partial class DemoJsonContext : JsonSerializerContext
{
    /// <summary>A canonical serialization of <paramref name="note"/>, used by the server to fingerprint operations.</summary>
    public static string Fingerprint(DemoNote note) => JsonSerializer.Serialize(note, Default.DemoNote);
}
