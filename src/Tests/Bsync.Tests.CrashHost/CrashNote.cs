using System.Text.Json.Serialization;
using Bsync;
using Bsync.Clocks;
using Bsync.Protocol;
using Bsync.Storage;
using Bsync.Storage.Sqlite;
using Bsync.Transport;

// Same JSON shape as the tests' Note type (property names are not changed by a naming policy).
public sealed class CrashNote : ISyncEntity
{
    public string Id { get; set; } = string.Empty;

    public HlcTimestamp UpdatedAt { get; set; }

    public bool Deleted { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;
}
