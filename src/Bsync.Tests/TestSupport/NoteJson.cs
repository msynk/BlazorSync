using System.Text.Json.Serialization;
using Bsync.Clocks;
using Bsync.Documents;
using Bsync.Server;

namespace Bsync.Tests.TestSupport;

/// <summary>Source-generated JSON metadata so tests exercise the trim/AOT-safe code paths.</summary>
[JsonSerializable(typeof(Note))]
[JsonSerializable(typeof(Protocol.PullRequest))]
[JsonSerializable(typeof(Protocol.PullResult<Note>))]
[JsonSerializable(typeof(Protocol.PushRequest<Note>))]
[JsonSerializable(typeof(Protocol.PushResult<Note>))]
public sealed partial class NoteJsonContext : JsonSerializerContext;

public static class NoteJson
{
    public static readonly Func<Note, Note> Clone = DocumentCloner.Json(NoteJsonContext.Default.Note);

    public static readonly Func<Note, string> Fingerprint = DocumentCloner.JsonFingerprint(NoteJsonContext.Default.Note);

    public static InMemorySyncServerOptions<Note> ServerOptions(
        IPhysicalClock? clock = null,
        Func<Protocol.PushOperation<Note>, Note?, string?>? validator = null,
        int maxOperationsPerPush = 1000) =>
        new()
        {
            Cloner = Clone,
            Fingerprint = Fingerprint,
            PhysicalClock = clock ?? new ManualClock(1_000),
            Validator = validator,
            MaxOperationsPerPush = maxOperationsPerPush,
        };
}
