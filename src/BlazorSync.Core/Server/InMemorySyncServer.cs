using BlazorSync.Core.Clocks;
using BlazorSync.Core.Documents;
using BlazorSync.Core.Protocol;

namespace BlazorSync.Core.Server;

/// <summary>
/// A reference, in-memory implementation of the server side of the protocol. It is the
/// authoritative "master": it orders writes with its own Hybrid Logical Clock, detects conflicts by
/// comparing the client's assumed-master version against the current version, and serves changes by
/// checkpoint. Phase 2 replaces this with an EF Core-backed server over an arbitrary database; the
/// semantics implemented here are the contract that backend must honour.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class InMemorySyncServer<TDocument>
    where TDocument : class, ISyncEntity
{
    private readonly Dictionary<string, TDocument> _master = new(StringComparer.Ordinal);
    private readonly Func<TDocument, TDocument> _clone;
    private readonly HybridLogicalClock _clock;
    private readonly object _gate = new();

    /// <summary>
    /// Creates a server with its own clock node and a deep-clone function. When
    /// <paramref name="cloner"/> is <see langword="null"/> a reflection-based JSON clone is used;
    /// supply an explicit cloner for trim/AOT-safe hosts.
    /// </summary>
#pragma warning disable IL2026, IL3050 // Default cloner is reflection-based; suppressed so a supplied cloner is warning-free.
    public InMemorySyncServer(string node = "server", Func<TDocument, TDocument>? cloner = null)
    {
        _clone = cloner ?? (static doc => DocumentCloner.JsonClone(doc));
        _clock = new HybridLogicalClock(node);
    }
#pragma warning restore IL2026, IL3050

    /// <summary>Serves the next batch of changes strictly after <paramref name="request"/>'s checkpoint.</summary>
    public PullResult<TDocument> Pull(PullRequest request)
    {
        lock (_gate)
        {
            var batch = _master.Values
                .Where(d => request.Since.IsBefore(d))
                .OrderBy(static d => d.UpdatedAt)
                .ThenBy(static d => d.Id, StringComparer.Ordinal)
                .Take(request.BatchSize)
                .Select(_clone)
                .ToList();

            var checkpoint = batch.Count > 0
                ? new Checkpoint(batch[^1].UpdatedAt, batch[^1].Id)
                : request.Since;

            var hasMore = batch.Count == request.BatchSize;
            return new PullResult<TDocument>(batch, checkpoint, hasMore);
        }
    }

    /// <summary>Applies a batch of client writes, returning accepted (re-stamped) and conflicting states.</summary>
    public PushResult<TDocument> Push(PushRequest<TDocument> request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var accepted = new List<TDocument>();
        var conflicts = new List<TDocument>();

        lock (_gate)
        {
            foreach (var row in request.Rows)
            {
                var id = row.NewDocument.Id;
                _master.TryGetValue(id, out var current);

                if (IsAccepted(current, row.AssumedMaster))
                {
                    var stored = _clone(row.NewDocument);
                    // Server is authoritative over the write timestamp: never trust the client clock.
                    stored.UpdatedAt = _clock.Update(row.NewDocument.UpdatedAt);
                    _master[id] = stored;
                    accepted.Add(_clone(stored));
                }
                else
                {
                    conflicts.Add(_clone(current!));
                }
            }
        }

        return new PushResult<TDocument>(accepted, conflicts);
    }

    private static bool IsAccepted(TDocument? current, TDocument? assumedMaster)
    {
        // No current server state: accept as an insert (or re-create).
        if (current is null)
        {
            return true;
        }

        // Current exists and the client based its write on the current version: accept the update.
        // The HLC UpdatedAt uniquely identifies a version, so equal timestamps mean "same base".
        return assumedMaster is not null && assumedMaster.UpdatedAt == current.UpdatedAt;
    }

    /// <summary>Returns a snapshot of the current master documents (test/diagnostic helper).</summary>
    public IReadOnlyList<TDocument> Snapshot(bool includeDeleted = true)
    {
        lock (_gate)
        {
            return _master.Values
                .Where(d => includeDeleted || !d.Deleted)
                .Select(_clone)
                .ToList();
        }
    }
}
