using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Running;
using Bsync.Clocks;
using Bsync.Conflicts;
using Bsync.Documents;
using Bsync.Server;
using Bsync.Storage;
using Bsync.Storage.Sqlite;

BenchmarkSwitcher.FromAssembly(typeof(Workload).Assembly).Run(args);
