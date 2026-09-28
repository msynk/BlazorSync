namespace Bsync.Demo.Services;

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
