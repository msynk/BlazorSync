using Bsync.Server.PostgreSql;
using Bsync.Tests.TestSupport;
using Npgsql;

namespace Bsync.Tests.PostgreSql;

/// <summary>A fresh database on the server named by <c>BSYNC_POSTGRES</c>, dropped on dispose.</summary>
public sealed class PostgresDatabase : IAsyncDisposable
{
    private readonly string _admin;

    private PostgresDatabase(string admin, string name)
    {
        _admin = admin;
        Name = name;
        ConnectionString = new NpgsqlConnectionStringBuilder(admin) { Database = name }.ToString();
        DataSource = NpgsqlDataSource.Create(ConnectionString);
    }

    public string Name { get; }

    public string ConnectionString { get; }

    public NpgsqlDataSource DataSource { get; }

    public static string AdminConnectionString =>
        Environment.GetEnvironmentVariable("BSYNC_POSTGRES") is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                "Set BSYNC_POSTGRES to a PostgreSQL connection string whose user may create databases, for example " +
                "\"Host=localhost;Port=5432;Username=postgres;Password=...\". These tests create and drop their own databases.");

    public static async Task<PostgresDatabase> CreateAsync()
    {
        var admin = AdminConnectionString;
        var name = $"bs_test_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
        await create.ExecuteNonQueryAsync();
        return new PostgresDatabase(admin, name);
    }

    /// <summary>Opens another pool on the same database, as a second server process would.</summary>
    public NpgsqlDataSource NewDataSource() => NpgsqlDataSource.Create(ConnectionString);

    public Task<PostgreSqlSyncAuthority<Note>> AuthorityAsync(
        string collection = "notes",
        NpgsqlDataSource? source = null,
        Func<PostgreSqlSyncAuthorityOptions<Note>, PostgreSqlSyncAuthorityOptions<Note>>? configure = null)
    {
        var options = new PostgreSqlSyncAuthorityOptions<Note>
        {
            DataSource = source ?? DataSource,
            DocumentType = NoteJsonContext.Default.Note,
            Collection = collection,
            PhysicalClock = Clocks.SystemPhysicalClock.Instance,
        };
        return PostgreSqlSyncAuthority<Note>.CreateAsync(configure is null ? options : configure(options));
    }

    public async ValueTask DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await using var connection = new NpgsqlConnection(_admin);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {Name} WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }
}

/// <summary>One database per test class.</summary>
public sealed class PostgresFixture : Xunit.IAsyncLifetime
{
    public PostgresDatabase Database { get; private set; } = null!;

    public async Task InitializeAsync() => Database = await PostgresDatabase.CreateAsync();

    public async Task DisposeAsync() => await Database.DisposeAsync();
}
