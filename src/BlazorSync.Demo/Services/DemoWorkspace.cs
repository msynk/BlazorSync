using BlazorSync.Server;
using BlazorSync.Demo.Models;

namespace BlazorSync.Demo.Services;

/// <summary>
/// The top-level singleton state for the playground: the shared in-process "cloud" server, the set
/// of simulated devices, and the activity log. Registered as a singleton so navigating between pages
/// preserves the scenario. Raises <see cref="Changed"/> after operations so subscribed components
/// re-render.
/// </summary>
public sealed class DemoWorkspace
{
    private static readonly string[] DeviceNames =
        { "Laptop", "Phone", "Tablet", "Desktop", "Watch", "Kiosk" };

    private readonly List<DeviceContext> _devices = new();
    private InMemorySyncServer<DemoNote> _server = null!;
    private int _deviceCounter;

    /// <summary>Creates the workspace with two devices to start.</summary>
    public DemoWorkspace()
    {
        Log = new ActivityLog();
        Reset();
    }

    /// <summary>Raised whenever devices or server state change.</summary>
    public event Action? Changed;

    /// <summary>The shared activity log.</summary>
    public ActivityLog Log { get; }

    /// <summary>The simulated devices.</summary>
    public IReadOnlyList<DeviceContext> Devices => _devices;

    /// <summary>The shared cloud server.</summary>
    public InMemorySyncServer<DemoNote> Server => _server;

    /// <summary>Adds another device (up to the pool of names).</summary>
    public DeviceContext AddDevice()
    {
        var name = _deviceCounter < DeviceNames.Length
            ? DeviceNames[_deviceCounter]
            : $"Device {_deviceCounter + 1}";
        var nodeId = $"{name.ToLowerInvariant()}-{Guid.CreateVersion7().ToString()[..8]}";
        _deviceCounter++;

        var device = new DeviceContext(name, nodeId, _server, Log);
        _devices.Add(device);
        Log.Add(ActivityKind.System, "workspace", $"added device \"{name}\"");
        NotifyChanged();
        return device;
    }

    /// <summary>Removes the most recently added device (keeps at least one).</summary>
    public void RemoveLastDevice()
    {
        if (_devices.Count <= 1)
        {
            return;
        }

        var device = _devices[^1];
        _devices.RemoveAt(_devices.Count - 1);
        _deviceCounter--;
        Log.Add(ActivityKind.System, "workspace", $"removed device \"{device.Name}\"");
        NotifyChanged();
    }

    /// <summary>Syncs every online device once (sequentially, so conflicts surface deterministically).</summary>
    public async Task SyncAllAsync()
    {
        foreach (var device in _devices.Where(d => d.IsOnline))
        {
            await device.SyncAsync();
        }

        NotifyChanged();
    }

    /// <summary>Tears everything down and starts fresh with two devices.</summary>
    public void Reset()
    {
        _devices.Clear();
        _deviceCounter = 0;
        _server = CreateServer();
        Log.Clear();
        Log.Add(ActivityKind.System, "workspace", "reset — fresh cloud and two devices");

        AddDeviceQuiet();
        AddDeviceQuiet();
        NotifyChanged();
    }

    /// <summary>Creates an in-process cloud server with trim/AOT-safe cloning and fingerprinting.</summary>
    public static InMemorySyncServer<DemoNote> CreateServer() =>
        new(new InMemorySyncServerOptions<DemoNote> { Cloner = static n => n.Clone(), Fingerprint = DemoJsonContext.Fingerprint }, "cloud");

    /// <summary>Notifies subscribers to re-render.</summary>
    public void NotifyChanged() => Changed?.Invoke();

    private void AddDeviceQuiet()
    {
        var name = DeviceNames[_deviceCounter];
        var nodeId = $"{name.ToLowerInvariant()}-{Guid.CreateVersion7().ToString()[..8]}";
        _deviceCounter++;
        _devices.Add(new DeviceContext(name, nodeId, _server, Log));
    }
}
