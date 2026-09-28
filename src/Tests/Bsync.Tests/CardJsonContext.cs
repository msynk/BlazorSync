using System.Text.Json.Serialization;
using Bsync.Conflicts;
using Bsync.Tests.TestSupport;
using Xunit;

namespace Bsync.Tests;

[JsonSerializable(typeof(Card))]
public sealed partial class CardJsonContext : JsonSerializerContext;
