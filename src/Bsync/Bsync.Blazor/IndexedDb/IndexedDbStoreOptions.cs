using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Bsync.Clocks;
using Microsoft.JSInterop;

namespace Bsync.Blazor.IndexedDb;

/// <summary>Options for <see cref="IndexedDbLocalStore{TDocument}"/>.</summary>
public sealed class IndexedDbStoreOptions
{
    /// <summary>
    /// IndexedDB database name. Include the signed-in account (for example <c>bsync-{userId}</c>) so
    /// different accounts on one browser profile never share a replica.
    /// </summary>
    public required string DatabaseName { get; init; }

    /// <summary>Collection name within the database. Default <c>default</c>.</summary>
    public string Collection { get; init; } = "default";

    /// <summary>How long opening waits for other tabs that block a schema upgrade. Default 10 seconds.</summary>
    public TimeSpan BlockedTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Maximum optimistic commit attempts when another tab writes the same records concurrently. Default 20.</summary>
    public int MaxCommitAttempts { get; init; } = 20;
}
