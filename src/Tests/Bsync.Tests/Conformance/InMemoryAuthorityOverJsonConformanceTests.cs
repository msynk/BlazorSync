using Bsync.Clocks;
using Bsync.Protocol;
using Bsync.Server;
using Bsync.Tests.TestSupport;
using Bsync.Transport;

namespace Bsync.Tests.Conformance;

/// <summary>The same suite with every message crossing the JSON wire encoding.</summary>
public sealed class InMemoryAuthorityOverJsonConformanceTests : AuthorityConformanceTests
{
    protected override ISyncTransport<Note> CreateAuthority(IPhysicalClock clock, Func<PushOperation<Note>, Note?, string?>? validator = null) =>
        new JsonWireTransport<Note>(new InProcessTransport<Note>(new InMemorySyncServer<Note>(NoteJson.ServerOptions(clock, validator))), NoteJsonContext.Default);
}
