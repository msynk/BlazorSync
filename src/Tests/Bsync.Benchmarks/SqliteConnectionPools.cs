using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Running;
using Bsync;
using Bsync.Clocks;
using Bsync.Conflicts;
using Bsync.Documents;
using Bsync.Server;
using Bsync.Storage;
using Bsync.Storage.Sqlite;

internal static class SqliteConnectionPools
{
    public static void ReleaseAll(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.db"))
        {
            SqliteStorePool.Release(file);
        }
    }
}
