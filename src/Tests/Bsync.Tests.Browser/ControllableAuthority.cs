using Bsync.Documents;
using Bsync.Protocol;
using Bsync.Server;
using Bsync.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Xunit;

namespace Bsync.Tests.Browser;

/// <summary>An authority whose backing in-memory server can be replaced (restore) and whose pushes can be delayed.</summary>
public sealed class ControllableAuthority : ISyncAuthority<ConformanceDocument>
{
    public ControllableAuthority() => Reset();

    public InMemorySyncServer<ConformanceDocument> Server { get; private set; } = null!;

    public TimeSpan PushDelay { get; set; }

    public AuthorityLimits Limits => Server.Limits;

    public static InMemorySyncServerOptions<ConformanceDocument> Options(InMemorySyncServerBackup<ConformanceDocument>? restoreFrom = null, long versionFloor = 0) => new()
    {
        Cloner = DocumentCloner.Json(ConformanceJsonContext.Default.ConformanceDocument),
        Fingerprint = DocumentCloner.JsonFingerprint(ConformanceJsonContext.Default.ConformanceDocument),
        RestoreFrom = restoreFrom,
        VersionFloor = versionFloor,
    };

    public void Reset() => Server = new InMemorySyncServer<ConformanceDocument>(Options());

    public void Restore(InMemorySyncServerBackup<ConformanceDocument> backup) =>
        Server = new InMemorySyncServer<ConformanceDocument>(Options(backup, Server.HighestVersion));

    public Task<PullResult<ConformanceDocument>> PullAsync(SyncCallContext context, PullRequest request, CancellationToken cancellationToken = default) =>
        Server.PullAsync(context, request, cancellationToken);

    public async Task<PushResult<ConformanceDocument>> PushAsync(SyncCallContext context, PushRequest<ConformanceDocument> request, CancellationToken cancellationToken = default)
    {
        if (PushDelay > TimeSpan.Zero)
        {
            await Task.Delay(PushDelay, cancellationToken);
        }

        return await Server.PushAsync(context, request, cancellationToken);
    }
}
