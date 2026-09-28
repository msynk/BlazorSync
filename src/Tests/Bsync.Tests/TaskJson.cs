using System.Text.Json.Serialization;
using Bsync.Documents;
using Bsync.Server;
using Bsync.Storage;
using Bsync.Storage.Sqlite;
using Bsync.Tests.Sqlite;
using Xunit;

namespace Bsync.Tests;

[JsonSerializable(typeof(TaskV1))]
[JsonSerializable(typeof(TaskV2))]
public sealed partial class TaskJson : JsonSerializerContext;
