using System.Text.Json;
using System.Text.Json.Serialization;
using Bsync.Clocks;
using Bsync.Tests.TestSupport;
using Xunit;

namespace Bsync.Tests;

public sealed class Card : ISyncEntity
{
    public string Id { get; set; } = "c1";

    public HlcTimestamp UpdatedAt { get; set; }

    public bool Deleted { get; set; }

    public string Title { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];

    public Address Address { get; set; } = new();

    public int Views { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Due { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}
