using Bsync.Client;
using Bsync.Clocks;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bsync.Blazor;

/// <summary>The server's clock for writes made on behalf of users (one per process).</summary>
public sealed class ServerSyncClock
{
    /// <summary>The clock; its node id is unique to this server process.</summary>
    public HybridLogicalClock Clock { get; } = new($"server-{Guid.NewGuid():N}"[..20]);
}
