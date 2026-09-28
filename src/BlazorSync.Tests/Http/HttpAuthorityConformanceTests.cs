using BlazorSync.Clocks;
using BlazorSync.Protocol;
using BlazorSync.Server;
using BlazorSync.Tests.Conformance;
using BlazorSync.Tests.TestSupport;
using BlazorSync.Transport;

namespace BlazorSync.Tests.Http;

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
