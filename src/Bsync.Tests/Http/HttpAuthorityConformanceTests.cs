using Bsync.Clocks;
using Bsync.Protocol;
using Bsync.Server;
using Bsync.Tests.Conformance;
using Bsync.Tests.TestSupport;
using Bsync.Transport;

namespace Bsync.Tests.Http;

/// <summary>The authority contract observed through the real HTTP endpoints and client.</summary>
public sealed class HttpAuthorityConformanceTests : AuthorityConformanceTests, IAsyncDisposable
{
    private readonly List<SyncTestHost> _hosts = [];

    protected override ISyncTransport<Note> CreateAuthority(IPhysicalClock clock, Func<PushOperation<Note>, Note?, string?>? validator = null)
    {
        var host = SyncTestHost.StartAsync(new InMemorySyncServer<Note>(NoteJson.ServerOptions(clock, validator))).GetAwaiter().GetResult();
        _hosts.Add(host);
        return host.Transport();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }
    }
}
