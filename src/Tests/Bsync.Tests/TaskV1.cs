using System.Text.Json;
using System.Text.Json.Serialization;
using Bsync.Clocks;
using Bsync.Server;
using Bsync.Storage;
using Bsync.Storage.Sqlite;
using Bsync.Tests.Sqlite;
using Xunit;

namespace Bsync.Tests;

/// <summary>Version 1 of an application's document: a "title".</summary>
public sealed class TaskV1 : ISyncEntity
{
    public string Id { get; set; } = string.Empty;

    public HlcTimestamp UpdatedAt { get; set; }

    public bool Deleted { get; set; }

    public string Title { get; set; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}
