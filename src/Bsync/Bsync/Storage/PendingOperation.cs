namespace Bsync.Storage;

/// <summary>An immutable, persisted push operation.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="OperationId">The globally unique operation id sent to the server.</param>
/// <param name="Revision">The <see cref="SyncRecord{TDocument}.LocalRevision"/> the payload was taken from.</param>
/// <param name="BaseVersion">The server version the operation is based on.</param>
/// <param name="Payload">The exact document state sent to the server.</param>
public sealed record PendingOperation<TDocument>(string OperationId, long Revision, long? BaseVersion, TDocument Payload)
    where TDocument : class, ISyncEntity
{
    /// <summary>The dependency group sent with the operation, if any.</summary>
    public string? Group { get; init; }

    /// <summary>The number of operations of <see cref="Group"/> sent together (0 without a group).</summary>
    public int GroupSize { get; init; }
}
