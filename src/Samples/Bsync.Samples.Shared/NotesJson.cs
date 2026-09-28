using System.Text.Json.Serialization;
using Bsync.Protocol;

namespace Bsync.Samples.Shared;

/// <summary>Source-generated JSON for notes and the protocol messages (trim/AOT safe).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Note))]
[JsonSerializable(typeof(PullRequest))]
[JsonSerializable(typeof(PullResult<Note>))]
[JsonSerializable(typeof(PushRequest<Note>))]
[JsonSerializable(typeof(PushResult<Note>))]
public sealed partial class NotesJson : JsonSerializerContext
{
    /// <summary>The application schema id sent with every sync request.</summary>
    public const string SchemaId = "notes-v1";
}
