namespace Bsync.Server.PostgreSql;

/// <summary>The database uses a newer Bsync schema than this version understands; nothing was changed.</summary>
/// <param name="message">The message.</param>
public sealed class PostgreSqlSchemaException(string message) : NotSupportedException(message);
