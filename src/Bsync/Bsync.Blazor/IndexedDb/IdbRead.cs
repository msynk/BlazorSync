using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Bsync.Clocks;
using Microsoft.JSInterop;

namespace Bsync.Blazor.IndexedDb;

internal sealed record IdbRead(List<IdbRecord?> Records, string? HighWater);
