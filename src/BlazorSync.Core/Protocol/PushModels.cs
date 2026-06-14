namespace BlazorSync.Core.Protocol;

/// <summary>
/// A single document write being pushed from a client to the server. Carries both the new state and
/// the master state the client believed was current, so the server can detect concurrent changes.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="AssumedMaster">
/// The server state the client last observed for this document, or <see langword="null"/> if the
/// client believes the document is new (an offline insert). If the server's current state differs,
/// the write is a conflict.
/// </param>
/// <param name="NewDocument">The new document state the client wants to become current.</param>
public sealed record PushRow<TDocument>(TDocument? AssumedMaster, TDocument NewDocument)
    where TDocument : class, ISyncEntity;

/// <summary>A request to push a batch of local writes to the server.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Rows">The writes to apply.</param>
public sealed record PushRequest<TDocument>(IReadOnlyList<PushRow<TDocument>> Rows)
    where TDocument : class, ISyncEntity;

/// <summary>
/// The result of a push. For each pushed row the server reports one of two outcomes:
/// <list type="bullet">
/// <item><description>
/// <see cref="Accepted"/> — the write was applied. The server returns its authoritative state with
/// the server-stamped <see cref="ISyncEntity.UpdatedAt"/>, which the client adopts as the new
/// baseline. This prevents the client's own write echoing back through pull as a false conflict.
/// </description></item>
/// <item><description>
/// <see cref="Conflicts"/> — the row's <see cref="PushRow{TDocument}.AssumedMaster"/> did not match
/// the server's current state. The server returns its current state for the client to resolve and
/// re-push.
/// </description></item>
/// </list>
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Accepted">The server-authoritative state of each accepted write.</param>
/// <param name="Conflicts">The current server state of each conflicting document.</param>
public sealed record PushResult<TDocument>(
    IReadOnlyList<TDocument> Accepted,
    IReadOnlyList<TDocument> Conflicts)
    where TDocument : class, ISyncEntity;
