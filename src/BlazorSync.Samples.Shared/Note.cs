using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorSync.Clocks;
using BlazorSync.Protocol;

namespace BlazorSync.Samples.Shared;

/// <summary>A synchronized note.</summary>
public sealed class Note : ISyncEntity
{
    /// <inheritdoc />
    public string Id { get; set; } = Guid.CreateVersion7().ToString("N");

    /// <inheritdoc />
    public HlcTimestamp UpdatedAt { get; set; }

    /// <inheritdoc />
    public bool Deleted { get; set; }

    /// <summary>The note text.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Fields added by newer versions of the app. Kept so that editing a note in an older version does not
    /// erase them (docs/protocol/v1.md §2.1).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

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
