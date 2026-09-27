using BlazorSync.Clocks;

namespace BlazorSync;

/// <summary>
/// Contract implemented by every entity that participates in synchronization. The three members
/// carry application-visible metadata: a stable identity, the origin timestamp of the current state
/// and a soft-delete flag. Replication metadata (server version, local revision, pending operation)
/// lives in the store's <c>SyncRecord</c> envelope, not on the entity.
/// </summary>
/// <remarks>
/// Deletions are never physical: a deleted record is retained with <see cref="Deleted"/> set to
/// <see langword="true"/> (a tombstone) so the deletion itself replicates to other replicas.
/// </remarks>
public interface ISyncEntity
{
    /// <summary>
    /// The stable, globally unique primary key (see <see cref="SyncIds.IsValid"/>). Should be
    /// assigned on creation by the originating client (for example a time-ordered GUID) so that
    /// inserts made offline do not collide.
    /// </summary>
    string Id { get; set; }

    /// <summary>
    /// The Hybrid Logical Clock timestamp at which the current state was authored (origin metadata).
    /// The authoring replica stamps it; the server validates it (bounded clock skew) but does not
    /// re-stamp it, so time-based policies such as last-write-wins compare authoring times rather
    /// than upload order. It is neither the server's concurrency token (the document version) nor the
    /// pull cursor (<see cref="Checkpoint"/>).
    /// </summary>
    HlcTimestamp UpdatedAt { get; set; }

    /// <summary>Whether this record represents a soft deletion (tombstone).</summary>
    bool Deleted { get; set; }
}
