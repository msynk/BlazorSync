using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bsync.Clocks;
using Microsoft.Data.Sqlite;

namespace Bsync.Storage.Sqlite;

/// <summary>Options for <see cref="SqliteLocalStore{TDocument}"/>.</summary>
public sealed class SqliteLocalStoreOptions
{
    /// <summary>Path of the database file. Created if missing.</summary>
    public required string DataSource { get; init; }

    /// <summary>
    /// Collection name. Several collections may share one database file; each has its own records,
    /// checkpoint, generation and clock high-water mark.
    /// </summary>
    public string Collection { get; init; } = "default";

    /// <summary>Durability level. Default <see cref="SqliteDurability.Full"/>.</summary>
    public SqliteDurability Durability { get; init; } = SqliteDurability.Full;

    /// <summary>How long a writer waits for another connection's write lock. Default 30 seconds.</summary>
    public TimeSpan BusyTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
