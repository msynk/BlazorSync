using Bsync.Protocol;
using Bsync.Transport;
using Bsync.Demo.Models;

namespace Bsync.Demo.Services;

/// <summary>Thrown by <see cref="SimulatedConnection"/> when a device is toggled offline.</summary>
public sealed class OfflineException : Exception
{
    /// <summary>Creates the exception.</summary>
    public OfflineException()
        : base("The device is offline. Local reads and writes still work; sync will resume when back online.")
    {
    }
}
