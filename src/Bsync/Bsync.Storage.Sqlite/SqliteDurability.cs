using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bsync.Clocks;
using Microsoft.Data.Sqlite;

namespace Bsync.Storage.Sqlite;

/// <summary>How a <see cref="SqliteLocalStore{TDocument}"/> trades write latency for durability.</summary>
public enum SqliteDurability
{
    /// <summary>
    /// <c>synchronous=FULL</c> in WAL mode: a committed write survives process termination and, subject to
    /// the storage hardware honouring flushes, power loss. Default.
    /// </summary>
    Full = 0,

    /// <summary>
    /// <c>synchronous=NORMAL</c> in WAL mode: a committed write survives process termination; the most
    /// recent commits may be lost (without corruption) on power loss or OS crash.
    /// </summary>
    Normal = 1,
}
