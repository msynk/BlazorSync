namespace BlazorSync.Demo.Services;

/// <summary>The severity/category of an <see cref="ActivityEntry"/>, used for colour-coding in the UI.</summary>
public enum ActivityKind
{
    /// <summary>A local read/write on a device.</summary>
    Local,

    /// <summary>A network sync operation.</summary>
    Sync,

    /// <summary>A conflict was detected and resolved.</summary>
    Conflict,

    /// <summary>An offline/connectivity event.</summary>
    Network,

    /// <summary>A workspace-level event (reset, device added).</summary>
    System,
}

/// <summary>A single timestamped entry in the activity log.</summary>
/// <param name="Time">When it happened (wall clock).</param>
/// <param name="Kind">The category.</param>
/// <param name="Source">The device or component that produced it.</param>
/// <param name="Message">Human-readable description.</param>
public sealed record ActivityEntry(DateTimeOffset Time, ActivityKind Kind, string Source, string Message);

/// <summary>
/// A small in-memory, observable activity log. Components subscribe to <see cref="Changed"/> to
/// re-render when new entries arrive.
/// </summary>
public sealed class ActivityLog
{
    private readonly List<ActivityEntry> _entries = new();
    private const int MaxEntries = 200;

    /// <summary>Raised whenever an entry is added or the log is cleared.</summary>
    public event Action? Changed;

    /// <summary>The entries, newest first.</summary>
    public IReadOnlyList<ActivityEntry> Entries => _entries;

    /// <summary>Adds an entry to the front of the log.</summary>
    public void Add(ActivityKind kind, string source, string message)
    {
        _entries.Insert(0, new ActivityEntry(DateTimeOffset.Now, kind, source, message));
        if (_entries.Count > MaxEntries)
        {
            _entries.RemoveAt(_entries.Count - 1);
        }

        Changed?.Invoke();
    }

    /// <summary>Clears all entries.</summary>
    public void Clear()
    {
        _entries.Clear();
        Changed?.Invoke();
    }
}
