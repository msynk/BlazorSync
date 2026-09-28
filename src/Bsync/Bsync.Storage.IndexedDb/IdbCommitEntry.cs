using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Bsync.Clocks;
using Microsoft.JSInterop;

namespace Bsync.Storage.IndexedDb;

internal sealed record IdbCommitEntry(string Id, IdbRecord? Record, string? ExpectedStamp);
