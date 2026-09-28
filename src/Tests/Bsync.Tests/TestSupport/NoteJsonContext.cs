using System.Text.Json.Serialization;
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
