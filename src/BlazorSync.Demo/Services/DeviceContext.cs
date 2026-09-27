using BlazorSync;
using BlazorSync.Clocks;
using BlazorSync.Server;
using BlazorSync.Storage;
using BlazorSync.Demo.Models;

namespace BlazorSync.Demo.Services;

/// <summary>
/// Represents one simulated device: its own local store, Hybrid Logical Clock, network connection
/// and sync engine, all wired to the shared in-process cloud server. This is the unit the UI renders
/// as a "device panel".
/// </summary>
public sealed class DeviceContext
{
    private readonly SyncEngine<DemoNote> _engine;
    private readonly SwitchableConflictHandler _conflictHandler;
    private readonly ActivityLog _log;

    /// <summary>Builds a device named <paramref name="name"/> bound to <paramref name="server"/>.</summary>
    public DeviceContext(string name, string nodeId, InMemorySyncServer<DemoNote> server, ActivityLog log)
    {
        Name = name;
        NodeId = nodeId;
        _log = log;

        var store = new InMemoryLocalStore<DemoNote>(static n => n.Clone());
        Connection = new SimulatedConnection(new InProcessTransport<DemoNote>(server));
        _conflictHandler = new SwitchableConflictHandler();
        var clock = new HybridLogicalClock(nodeId);

        _engine = new SyncEngine<DemoNote>(
            store,
            Connection,
            clock,
            _conflictHandler,
            new SyncOptions<DemoNote> { Cloner = static n => n.Clone() });
    }

    /// <summary>The device's display name.</summary>
    public string Name { get; }

    /// <summary>The device's HLC node id (also used to tag its writes).</summary>
    public string NodeId { get; }

    /// <summary>The simulated network connection (online toggle + latency).</summary>
    public SimulatedConnection Connection { get; }

    /// <summary>Whether the device is currently online.</summary>
    public bool IsOnline
    {
        get => Connection.IsOnline;
        set
        {
            if (Connection.IsOnline == value)
            {
                return;
            }

            Connection.IsOnline = value;
            _log.Add(ActivityKind.Network, Name, value ? "went online" : "went offline");
        }
    }

    /// <summary>The active conflict strategy for this device.</summary>
    public ConflictStrategy Strategy
    {
        get => _conflictHandler.Strategy;
        set => _conflictHandler.Strategy = value;
    }

    /// <summary>The result of the most recent sync, if any.</summary>
    public SyncResult? LastSync { get; private set; }

    /// <summary>A short human-readable status line.</summary>
    public string Status { get; private set; } = "Ready";

    /// <summary>Number of local writes waiting to be pushed.</summary>
    public int PendingCount { get; private set; }

    /// <summary>Creates a new note locally (instant, offline-capable).</summary>
    public async Task CreateNoteAsync(string title, string body, string category)
    {
        var note = new DemoNote { Title = title, Body = body, Category = category };
        await _engine.WriteAsync(note);
        _log.Add(ActivityKind.Local, Name, $"created \"{Trim(title)}\"");
        await RefreshPendingAsync();
    }

    /// <summary>Applies an edit to an existing note locally.</summary>
    public async Task UpdateNoteAsync(DemoNote note)
    {
        await _engine.WriteAsync(note);
        _log.Add(ActivityKind.Local, Name, $"edited \"{Trim(note.Title)}\"");
        await RefreshPendingAsync();
    }

    /// <summary>Soft-deletes a note locally.</summary>
    public async Task DeleteNoteAsync(DemoNote note)
    {
        await _engine.DeleteAsync(note.Id);
        _log.Add(ActivityKind.Local, Name, $"deleted \"{Trim(note.Title)}\"");
        await RefreshPendingAsync();
    }

    /// <summary>Runs a full sync (pull then push). Returns false and stays usable if offline.</summary>
    public async Task<bool> SyncAsync()
    {
        try
        {
            Status = "Syncing…";
            var result = await _engine.SyncAsync();
            LastSync = result;
            await RefreshPendingAsync();

            var detail = $"pulled {result.Pulled}, pushed {result.Pushed}";
            if (result.Conflicts > 0)
            {
                _log.Add(ActivityKind.Conflict, Name, $"resolved {result.Conflicts} conflict(s) using {Strategy}");
                detail += $", {result.Conflicts} conflict(s) via {Strategy}";
            }

            if (result.Rejected > 0)
            {
                detail += $", {result.Rejected} rejected";
            }

            if (result.IsComplete)
            {
                Status = $"Synced · pulled {result.Pulled}, pushed {result.Pushed}";
                _log.Add(ActivityKind.Sync, Name, $"sync complete ({detail})");
            }
            else
            {
                // A normal return is not proof that everything was sent; say so.
                Status = "Partially synced · work remains";
                _log.Add(ActivityKind.Sync, Name, $"sync incomplete ({detail}, {result.Deferred} deferred)");
            }
            return true;
        }
        catch (OfflineException)
        {
            Status = "Offline — sync deferred";
            _log.Add(ActivityKind.Network, Name, "sync skipped (offline)");
            return false;
        }
    }

    /// <summary>Returns the app-visible notes (optionally including soft-deleted ones).</summary>
    public Task<IReadOnlyList<DemoNote>> GetNotesAsync(bool includeDeleted = false) =>
        _engine.QueryAsync(includeDeleted);

    /// <summary>
    /// Returns notes paired with their dirty (unpushed) state, for rendering. Ordered with pending
    /// changes first, then by title.
    /// </summary>
    public async Task<IReadOnlyList<NoteView>> GetViewsAsync(bool includeDeleted = false)
    {
        var notes = await _engine.QueryAsync(includeDeleted);
        var views = new List<NoteView>(notes.Count);
        foreach (var note in notes)
        {
            var record = await _engine.GetAsync(note.Id);
            views.Add(new NoteView(note, record is { IsDirty: true }));
        }

        return views
            .OrderByDescending(v => v.IsDirty)
            .ThenBy(v => v.Note.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Returns the sync record (with dirty/base metadata) for a note, for inspection.</summary>
    public Task<SyncRecord<DemoNote>?> GetRecordAsync(string id) => _engine.GetAsync(id);

    private async Task RefreshPendingAsync() => PendingCount = await _engine.CountDirtyAsync();

    private static string Trim(string value) =>
        value.Length <= 24 ? value : value[..24] + "…";
}
