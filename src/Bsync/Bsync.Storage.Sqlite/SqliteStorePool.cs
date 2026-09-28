using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bsync.Clocks;
using Microsoft.Data.Sqlite;

namespace Bsync.Storage.Sqlite;

/// <summary>Connection-pool helpers.</summary>
public static class SqliteStorePool
{
    /// <summary>
    /// Closes the pooled connections to one database file (for example before copying, restoring or deleting it).
    /// Unlike <see cref="SqliteConnection.ClearAllPools"/>, it does not touch other databases' connections.
    /// </summary>
    public static void Release(string dataSource)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataSource);
        using var connection = new SqliteConnection(ConnectionString(dataSource));
        SqliteConnection.ClearPool(connection);
    }

    internal static string ConnectionString(string dataSource) => new SqliteConnectionStringBuilder
    {
        DataSource = dataSource,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = true,
    }.ToString();
}
