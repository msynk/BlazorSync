using BlazorSync.Clocks;

namespace BlazorSync;

/// <summary>
/// Contract implemented by every entity that participates in synchronization. The three members
/// carry the minimal metadata the sync protocol needs: a stable identity, a causal write timestamp
/// and a soft-delete flag.
/// </summary>
/// <remarks>
/// Following the "complexity in the client, dumb backend" design, deletions are never physical:
/// a deleted record is retained with <see cref="Deleted"/> set to <see langword="true"/> so the
/// deletion itself can replicate to other peers.
/// </remarks>
public interface ISyncEntity
{
    /// <summary>
    /// The stable, globally unique primary key. Should be assigned on creation by the originating
    /// client (for example a time-ordered GUID) so that inserts made offline do not collide.
    /// </summary>
    string Id { get; set; }

    /// <summary>
    /// The Hybrid Logical Clock timestamp of the most recent write to this record. Used both as the
    /// sync cursor (records are pulled in ascending timestamp order) and for conflict detection.
    /// The server is authoritative: it re-stamps this value when it accepts a write.
    /// </summary>
    HlcTimestamp UpdatedAt { get; set; }

    /// <summary>Whether this record represents a soft deletion.</summary>
    bool Deleted { get; set; }
}
