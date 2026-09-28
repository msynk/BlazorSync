using System.Security.Claims;
using Bsync.Clocks;
using Bsync.Server;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Bsync.Blazor;

/// <summary>
/// Explicit registration recipes, one per hosting profile (docs/architecture/adr-007). Register exactly one
/// <see cref="ISyncCollection{TDocument}"/> per runtime: in a Blazor Web App the server project registers the
/// server-connected collection and the WebAssembly client project registers the local one; each runtime has its
/// own container, so Auto render mode gets the right one on each side.
/// </summary>
public static class BsyncServiceCollectionExtensions
{
    /// <summary>
    /// Server-connected profile (Interactive Server, prerendering, static SSR): a scoped collection per circuit
    /// or request that calls <paramref name="authority"/> in-process as the authenticated user.
    /// </summary>
    /// <param name="services">The server's services.</param>
    /// <param name="authority">Resolves the authority (it must also implement <see cref="ISyncDocumentReader{TDocument}"/>).</param>
    /// <param name="cloner">Deep-clone function for documents.</param>
    /// <param name="resolveScope">
    /// Derives the scope from the authenticated user; return <see langword="null"/> to deny. Default: <c>"default"</c>.
    /// </param>
    public static IServiceCollection AddServerSyncCollection<TDocument>(
        this IServiceCollection services,
        Func<IServiceProvider, ISyncAuthority<TDocument>> authority,
        Func<TDocument, TDocument> cloner,
        Func<ClaimsPrincipal, string?>? resolveScope = null)
        where TDocument : class, ISyncEntity
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(cloner);
        resolveScope ??= static _ => "default";

        services.TryAddSingleton(_ => new ServerSyncClock());
        services.AddScoped<ISyncCollection<TDocument>>(sp =>
        {
            var authentication = sp.GetService<AuthenticationStateProvider>();
            return new ServerSyncCollection<TDocument>(
                authority(sp),
                async _ =>
                {
                    var user = authentication is null
                        ? new ClaimsPrincipal(new ClaimsIdentity())
                        : (await authentication.GetAuthenticationStateAsync().ConfigureAwait(false)).User;
                    var scope = resolveScope(user)
                        ?? throw new SyncTransportException(SyncErrorCodes.Forbidden, "No scope is available for the current user.", isTransient: false);
                    return new SyncCallContext(user, scope);
                },
                sp.GetRequiredService<ServerSyncClock>().Clock,
                cloner);
        });
        return services;
    }

    /// <summary>
    /// Local-replica profile (Blazor WebAssembly, MAUI/WPF/WinForms Hybrid, headless): one session and collection
    /// per app instance. Throws if called in an ASP.NET Core container, where a singleton replica would be
    /// shared by every user.
    /// </summary>
    /// <param name="services">The client's services.</param>
    /// <param name="options">Builds the session options (replica, transport, lease).</param>
    /// <param name="resolveAccount">Returns the signed-in account; default <c>"default"</c>.</param>
    public static IServiceCollection AddLocalSyncCollection<TDocument>(
        this IServiceCollection services,
        Func<IServiceProvider, SyncSessionOptions<TDocument>> options,
        Func<IServiceProvider, CancellationToken, Task<string>>? resolveAccount = null)
        where TDocument : class, ISyncEntity
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        if (services.Any(d => d.ServiceType.FullName == "Microsoft.AspNetCore.Hosting.IWebHostEnvironment"))
        {
            throw new InvalidOperationException(
                "A local replica is per device. Register AddServerSyncCollection on the server and AddLocalSyncCollection in the WebAssembly or native client.");
        }

        services.AddSingleton(sp =>
        {
            var built = options(sp);
            return new SyncSession<TDocument>(built.Logger is null && sp.GetService<ILoggerFactory>() is { } logging
                ? built with { Logger = logging.CreateLogger("Bsync.SyncSession") }
                : built);
        });
        services.AddSingleton<ISyncCollection<TDocument>>(sp => new LocalSyncCollection<TDocument>(
            sp.GetRequiredService<SyncSession<TDocument>>(),
            resolveAccount is null ? static _ => Task.FromResult("default") : ct => resolveAccount(sp, ct)));
        return services;
    }
}

/// <summary>The server's clock for writes made on behalf of users (one per process).</summary>
public sealed class ServerSyncClock
{
    /// <summary>The clock; its node id is unique to this server process.</summary>
    public HybridLogicalClock Clock { get; } = new($"server-{Guid.NewGuid():N}"[..20]);
}
