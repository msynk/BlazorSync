namespace Bsync.Client;

/// <summary>Runs an action once on dispose.</summary>
internal sealed class Unsubscriber(Action action) : IDisposable
{
    private Action? _action = action;

    public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
}
