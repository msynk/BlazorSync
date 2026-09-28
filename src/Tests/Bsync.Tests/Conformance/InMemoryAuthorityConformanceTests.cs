using Bsync.Clocks;
using Bsync.Protocol;
using Bsync.Server;
using Bsync.Tests.TestSupport;
using Bsync.Transport;

namespace Bsync.Tests.Conformance;

public sealed class InMemoryAuthorityConformanceTests : AuthorityConformanceTests
{
    protected override ISyncTransport<Note> CreateAuthority(IPhysicalClock clock, Func<PushOperation<Note>, Note?, string?>? validator = null) =>
        new InProcessTransport<Note>(new InMemorySyncServer<Note>(NoteJson.ServerOptions(clock, validator)));
}
