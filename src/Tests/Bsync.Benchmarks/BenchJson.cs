using System.Text.Json.Serialization;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Running;
using Bsync;
using Bsync.Clocks;
using Bsync.Conflicts;
using Bsync.Documents;
using Bsync.Server;
using Bsync.Storage;
using Bsync.Storage.Sqlite;

[JsonSerializable(typeof(BenchDocument))]
public sealed partial class BenchJson : JsonSerializerContext;
