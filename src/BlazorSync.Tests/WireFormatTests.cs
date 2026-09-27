using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using BlazorSync.Clocks;
using BlazorSync.Protocol;
using BlazorSync.Tests.TestSupport;
using Xunit;

namespace BlazorSync.Tests;

/// <summary>A document type exercising the JSON rules of docs/protocol/v1.md.</summary>
public sealed class FixtureDoc : ISyncEntity
{
    public string Id { get; set; } = string.Empty;

    public HlcTimestamp UpdatedAt { get; set; }

    public bool Deleted { get; set; }

    public string? Title { get; set; }

    public decimal Amount { get; set; }

    public DateTimeOffset? Due { get; set; }

    public List<string> Tags { get; set; } = [];

    /// <summary>Fields unknown to this client; preserved so a full-document write does not erase them (I17).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PullRequest))]
[JsonSerializable(typeof(PullResult<FixtureDoc>))]
[JsonSerializable(typeof(PushRequest<FixtureDoc>))]
[JsonSerializable(typeof(PushResult<FixtureDoc>))]
public sealed partial class FixtureJsonContext : JsonSerializerContext;

/// <summary>Round-trips the normative wire fixtures and rejects the invalid ones (Phase 2, ADR-011).</summary>
public sealed class WireFormatTests
{
    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "fixtures");

    public static TheoryData<string> Valid() => Files("valid");

    public static TheoryData<string> Invalid() => Files("invalid");

    [Theory(DisplayName = "I17 I12: valid fixtures round-trip byte-for-byte in meaning")]
    [MemberData(nameof(Valid))]
    public void ValidFixturesRoundTrip(string name)
    {
        var original = File.ReadAllText(Path.Combine(FixtureRoot, "valid", name));
        var typeInfo = TypeInfoFor(name);

        var value = JsonSerializer.Deserialize(original, typeInfo);
        var written = JsonSerializer.Serialize(value, typeInfo);

        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(written)),
            $"Round trip changed {name}:\n{written}");
    }

    [Theory(DisplayName = "I09: invalid fixtures are refused")]
    [MemberData(nameof(Invalid))]
    public void InvalidFixturesAreRefused(string name)
    {
        var json = File.ReadAllText(Path.Combine(FixtureRoot, "invalid", name));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, TypeInfoFor(name)));
    }

    [Fact(DisplayName = "I12: 64-bit versions, non-ASCII ids, unknown fields and every outcome kind decode exactly")]
    public void DecodedValues()
    {
        var page = Read<PullResult<FixtureDoc>>("pull-response.page.json");
        Assert.Equal(9_007_199_254_740_993L, page.Changes[0].Version);
        Assert.Equal("ノート-😀", page.Changes[0].Document.Id);
        Assert.Equal(12345678901234567.89m, page.Changes[0].Document.Amount);
        Assert.Equal(new HlcTimestamp(1_790_000_000_000, 0, "device-a"), page.Changes[0].Document.UpdatedAt);
        Assert.True(page.Changes[0].Document.Unknown!.ContainsKey("colour"));
        Assert.True(page.Changes[1].Document.Deleted);
        Assert.True(page.HasMore);

        var start = Read<PullRequest>("pull-request.start.json");
        Assert.True(start.Since.IsStart);

        var push = Read<PushRequest<FixtureDoc>>("push-request.json");
        Assert.Null(push.Operations[0].BaseVersion);
        Assert.Equal(9_007_199_254_740_993L, push.Operations[1].BaseVersion);

        var result = Read<PushResult<FixtureDoc>>("push-response.json");
        Assert.Equal(
            [PushOutcomeKind.Accepted, PushOutcomeKind.Conflict, PushOutcomeKind.Rejected, PushOutcomeKind.RetryLater, PushOutcomeKind.Accepted],
            result.Outcomes.Select(o => o.Kind));
        Assert.True(result.Outcomes[4].IsDuplicate);
        Assert.False(result.Outcomes[0].IsDuplicate);
        Assert.Null(result.Outcomes[2].Document);
    }

    [Fact(DisplayName = "I17: unknown envelope members are ignored (forward compatibility)")]
    public void UnknownEnvelopeMembersIgnored()
    {
        const string json = """{ "outcomes": [ { "operationId": "o", "kind": "retry-later", "errorCode": "unavailable", "retryAfterMs": 500 } ], "serverTime": "x" }""";
        var result = JsonSerializer.Deserialize(json, FixtureJsonContext.Default.PushResultFixtureDoc)!;
        Assert.Equal(PushOutcomeKind.RetryLater, result.Outcomes.Single().Kind);
    }

    [Fact(DisplayName = "I17: unknown document fields survive a local edit and push")]
    public void UnknownFieldsSurviveEdit()
    {
        var page = Read<PullResult<FixtureDoc>>("pull-response.page.json");
        var doc = page.Changes[0].Document;
        doc.Title = "edited";

        var op = new PushOperation<FixtureDoc>("op", doc.Id, page.Changes[0].Version, doc);
        var json = JsonSerializer.Serialize(new PushRequest<FixtureDoc>([op]), FixtureJsonContext.Default.PushRequestFixtureDoc);

        Assert.Contains("\"colour\"", json);
        Assert.Contains("\"baseVersion\":\"9007199254740993\"", json);
    }

    [Fact(DisplayName = "T11 I04 I20: engines converge when every message crosses the JSON wire encoding")]
    public async Task EngineTrafficRoundTripsThroughJson()
    {
        var server = InMemorySyncServerRef.Create();
        var a = new TestReplica(server, "a", transport: t => new JsonWireTransport<Note>(t, NoteJsonContext.Default));
        var b = new TestReplica(server, "b", transport: t => new JsonWireTransport<Note>(t, NoteJsonContext.Default));

        await a.Engine.WriteAsync(new Note { Id = "ノート-😀", Title = "hello" });
        a.Transport.LoseResponses = 1;
        await Assert.ThrowsAsync<InjectedFaultException>(() => a.Engine.SyncAsync());
        Assert.True((await a.Engine.SyncAsync()).IsComplete);
        await b.Engine.SyncAsync();
        await b.Engine.DeleteAsync("ノート-😀");
        await b.Engine.SyncAsync();
        await a.Engine.SyncAsync();

        Assert.True((await a.RecordAsync("ノート-😀")).Current.Deleted);
        Assert.Equal(2, server.Server.ReceiptCount);
    }

    private static T Read<T>(string name) =>
        (T)JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(FixtureRoot, "valid", name)), TypeInfoFor(name))!;

    private static JsonTypeInfo TypeInfoFor(string name) => name.Split('.')[0] switch
    {
        "pull-request" => FixtureJsonContext.Default.PullRequest,
        "pull-response" => FixtureJsonContext.Default.PullResultFixtureDoc,
        "push-request" => FixtureJsonContext.Default.PushRequestFixtureDoc,
        "push-response" => FixtureJsonContext.Default.PushResultFixtureDoc,
        var other => throw new InvalidOperationException($"Unknown fixture type '{other}'."),
    };

    private static TheoryData<string> Files(string folder)
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(FixtureRoot, folder), "*.json").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }
}
