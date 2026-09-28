using BlazorSync.Blazor;
using BlazorSync.Samples.Shared;

namespace BlazorSync.Samples.Hybrid.Maui;

public partial class App : Application
{
    private readonly SyncSession<Note> _session;

    public App(SyncSession<Note> session)
    {
        InitializeComponent();
        _session = session;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage()) { Title = "BlazorSync notes (MAUI)" };

        // Native lifecycle (ADR-007): no replication while the app is in the background; resuming syncs at once.
        window.Stopped += (_, _) => _session.Pause();
        window.Resumed += (_, _) => _session.Resume();
        return window;
    }
}
