using System.Text.Json.Serialization;
using Bsync.Documents;
using Bsync.Protocol;
using Bsync.Storage;
using Bsync.Blazor.IndexedDb;
using Bsync.Testing;
using Bsync.Transport;
using Microsoft.JSInterop;

namespace Bsync.Tests.BrowserHost;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<CaseResult>))]
[JsonSerializable(typeof(SyncSummary))]
[JsonSerializable(typeof(List<DocumentView>))]
internal sealed partial class HarnessJson : JsonSerializerContext;
