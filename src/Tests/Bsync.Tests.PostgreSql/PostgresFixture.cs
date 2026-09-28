using Bsync.Tests.TestSupport;
using Npgsql;

namespace Bsync.Tests.PostgreSql;

/// <summary>One database per test class.</summary>
public sealed class PostgresFixture : Xunit.IAsyncLifetime
{
    public PostgresDatabase Database { get; private set; } = null!;

    public async Task InitializeAsync() => Database = await PostgresDatabase.CreateAsync();

    public async Task DisposeAsync() => await Database.DisposeAsync();
}
