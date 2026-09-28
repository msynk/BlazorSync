using System.Text.Json;
using System.Text.Json.Serialization;
using Bsync.Clocks;
using Bsync.Documents;
using Bsync.Storage;
using Bsync.Storage.Sqlite;
using Bsync.Tests.Sqlite;
using Xunit;

namespace Bsync.Tests;

/// <summary>Version 2 renamed "title" to "heading" and added "done"; it upgrades version 1 documents when reading them.</summary>
public sealed class TaskV2 : ISyncEntity, IJsonOnDeserialized
{
    public string Id { get; set; } = string.Empty;

    public HlcTimestamp UpdatedAt { get; set; }

    public bool Deleted { get; set; }

    public string Heading { get; set; } = string.Empty;

    public bool Done { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    void IJsonOnDeserialized.OnDeserialized()
    {
        if (DocumentUpgrade.TryTake(Unknown, "Title", out var title) && Heading.Length == 0)
        {
            Heading = title.GetString() ?? string.Empty;
        }
    }
}
