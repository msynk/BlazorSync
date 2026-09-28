using System.Text.Encodings.Web;
using Bsync.Protocol;
using Bsync.Server;
using Bsync.Tests.TestSupport;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsync.Tests.Http;

/// <summary>An authority that always delegates to the server an <see cref="InMemorySyncServerRef"/> currently holds (so restores are visible over HTTP).</summary>
public sealed class RefAuthority(InMemorySyncServerRef server) : ISyncAuthority<Note>
{
    public AuthorityLimits Limits => server.Server.Limits;

    public Task<PullResult<Note>> PullAsync(SyncCallContext context, PullRequest request, CancellationToken cancellationToken = default) =>
        server.Server.PullAsync(context, request, cancellationToken);

    public Task<PushResult<Note>> PushAsync(SyncCallContext context, PushRequest<Note> request, CancellationToken cancellationToken = default) =>
        server.Server.PushAsync(context, request, cancellationToken);
}
