using System.Text.Json.Serialization;
using Bsync.Clocks;
using Bsync.Documents;
using Bsync.Protocol;
using Bsync.Storage;
using Bsync.Storage.Sqlite;
using Bsync.Transport;

[JsonSerializable(typeof(CrashNote))]
public sealed partial class CrashJson : JsonSerializerContext;
