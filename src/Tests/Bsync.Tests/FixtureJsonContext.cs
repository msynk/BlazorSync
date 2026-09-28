using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Bsync.Clocks;
using Bsync.Protocol;
using Xunit;

namespace Bsync.Tests;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PullRequest))]
[JsonSerializable(typeof(PullResult<FixtureDoc>))]
[JsonSerializable(typeof(PushRequest<FixtureDoc>))]
[JsonSerializable(typeof(PushResult<FixtureDoc>))]
public sealed partial class FixtureJsonContext : JsonSerializerContext;
