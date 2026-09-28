using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bsync.Clocks;
using Microsoft.Data.Sqlite;

namespace Bsync.Storage.Sqlite;

/// <summary>Thrown when a database was created by a newer, unsupported version of the store.</summary>
public sealed class SqliteStoreSchemaException(string message) : NotSupportedException(message);
