using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Bsync.Clocks;
using Microsoft.JSInterop;

namespace Bsync.Blazor.IndexedDb;

internal sealed record IdbMeta(string? Checkpoint, string Generation, bool Resnapshot, bool PurgeMissing, string? HighWater, string? ReplicaId, string? Incarnation);
