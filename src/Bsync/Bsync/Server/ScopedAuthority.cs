using Bsync.Protocol;

namespace Bsync.Server;

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
