using System.Security.Claims;
using BlazorSync.Protocol;

namespace BlazorSync.Server;

/// <summary>
/// Who is calling an authority and in which scope. Built by the host from authenticated state (never from
/// request bodies), and passed identically by HTTP endpoints and in-process callers so both enforce the
/// same rules (I18).
/// </summary>
/// <param name="Principal">The authenticated caller. Unauthenticated callers have an identity with <c>IsAuthenticated == false</c>.</param>
/// <param name="Scope">
/// The data partition the caller may see (for example a tenant id), derived by the host from
/// <paramref name="Principal"/>. Feeds, receipts and checkpoints never cross scopes.
/// </param>
public sealed record SyncCallContext(ClaimsPrincipal Principal, string Scope)
{
    /// <summary>An unauthenticated caller in the <c>default</c> scope (tests and single-user hosts).</summary>
    public static SyncCallContext Anonymous { get; } = new(new ClaimsPrincipal(new ClaimsIdentity()), "default");
}

/// <summary>Limits an authority enforces and advertises.</summary>
/// <param name="MaxOperationsPerPush">Maximum operations in one push request.</param>
/// <param name="MaxPageSize">Maximum changes returned by one pull.</param>
public sealed record AuthorityLimits(int MaxOperationsPerPush, int MaxPageSize);

/// <summary>
/// The server side of the protocol for one collection (docs/protocol/v1.md §4 and §6). Implementations
/// must pass <c>AuthorityConformanceTests</c>.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public interface ISyncAuthority<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <summary>The limits this authority enforces.</summary>
    AuthorityLimits Limits { get; }

    /// <summary>Serves the next page of the caller's feed.</summary>
    /// <exception cref="SyncResetRequiredException">The checkpoint cannot be served.</exception>
    /// <exception cref="SyncProtocolException">The request is malformed.</exception>
    Task<PullResult<TDocument>> PullAsync(SyncCallContext context, PullRequest request, CancellationToken cancellationToken = default);

    /// <summary>Decides each operation and returns one outcome per operation, in order.</summary>
    /// <exception cref="SyncTransportException">The request exceeds <see cref="Limits"/> (<c>payload-too-large</c>).</exception>
    Task<PushResult<TDocument>> PushAsync(SyncCallContext context, PushRequest<TDocument> request, CancellationToken cancellationToken = default);
}

/// <summary>A document as currently stored by an authority, with its version.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Document">The current state (tombstones included only when asked for).</param>
/// <param name="Version">The server version, for use as the base of a later write.</param>
public sealed record StoredDocument<TDocument>(TDocument Document, long Version)
    where TDocument : class, ISyncEntity;

/// <summary>
/// Authorized direct reads from an authority, for server-connected hosts (Interactive Server, prerendering,
/// static SSR) that show current server state without a local replica. Applies the same read authorization
/// as the change feed.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public interface ISyncDocumentReader<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <summary>Returns the document if it exists and the caller may read it.</summary>
    Task<StoredDocument<TDocument>?> GetAsync(SyncCallContext context, string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns up to <paramref name="limit"/> readable, non-deleted documents in id order, starting after
    /// <paramref name="afterId"/> (keyset pagination).
    /// </summary>
    Task<IReadOnlyList<StoredDocument<TDocument>>> ListAsync(SyncCallContext context, int limit, string? afterId = null, CancellationToken cancellationToken = default);
}

/// <summary>Published by an authority after a push committed changes. A hint only: it may be missed.</summary>
/// <param name="Scope">The scope whose documents changed.</param>
/// <param name="Ids">The changed document ids.</param>
public sealed record AuthorityCommit(string Scope, IReadOnlyList<string> Ids);

/// <summary>
/// An authority that announces commits, so connected hosts can refresh promptly. Subscribers must treat
/// announcements as hints and still reconcile from durable state (I13).
/// </summary>
public interface ISyncCommitNotifier
{
    /// <summary>Raised after changes commit. Handlers run synchronously on the committing thread and must be quick.</summary>
    event Action<AuthorityCommit>? Committed;
}

/// <summary>
/// Routes each call to a separate authority per <see cref="SyncCallContext.Scope"/>, so feeds, versions and
/// operation receipts of different scopes (tenants) are fully isolated (I07).
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class ScopedAuthority<TDocument> : ISyncAuthority<TDocument>, ISyncDocumentReader<TDocument>, ISyncCommitNotifier
    where TDocument : class, ISyncEntity
{
    private readonly Func<string, ISyncAuthority<TDocument>> _factory;
    private readonly Dictionary<string, ISyncAuthority<TDocument>> _authorities = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Creates the router. <paramref name="factory"/> is called once per scope.</summary>
    public ScopedAuthority(Func<string, ISyncAuthority<TDocument>> factory, AuthorityLimits limits)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(limits);
        _factory = factory;
        Limits = limits;
    }

    /// <inheritdoc />
    public AuthorityLimits Limits { get; }

    /// <inheritdoc />
    public Task<PullResult<TDocument>> PullAsync(SyncCallContext context, PullRequest request, CancellationToken cancellationToken = default) =>
        For(context).PullAsync(context, request, cancellationToken);

    /// <inheritdoc />
    public Task<PushResult<TDocument>> PushAsync(SyncCallContext context, PushRequest<TDocument> request, CancellationToken cancellationToken = default) =>
        For(context).PushAsync(context, request, cancellationToken);

    /// <inheritdoc />
    public event Action<AuthorityCommit>? Committed;

    /// <inheritdoc />
    public Task<StoredDocument<TDocument>?> GetAsync(SyncCallContext context, string id, CancellationToken cancellationToken = default) =>
        Reader(context).GetAsync(context, id, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredDocument<TDocument>>> ListAsync(SyncCallContext context, int limit, string? afterId = null, CancellationToken cancellationToken = default) =>
        Reader(context).ListAsync(context, limit, afterId, cancellationToken);

    private ISyncDocumentReader<TDocument> Reader(SyncCallContext context) =>
        For(context) as ISyncDocumentReader<TDocument>
        ?? throw new NotSupportedException("The per-scope authority does not support direct reads.");

    private ISyncAuthority<TDocument> For(SyncCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!SyncIds.IsValid(context.Scope))
        {
            throw new SyncTransportException(SyncErrorCodes.Forbidden, "No valid scope for the caller.", isTransient: false);
        }

        lock (_gate)
        {
            if (!_authorities.TryGetValue(context.Scope, out var authority))
            {
                authority = _factory(context.Scope);
                _authorities[context.Scope] = authority;
                if (authority is ISyncCommitNotifier notifier)
                {
                    notifier.Committed += commit => Committed?.Invoke(commit);
                }
            }

            return authority;
        }
    }
}
