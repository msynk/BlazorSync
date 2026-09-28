using Microsoft.Playwright;
using Npgsql;
using Xunit;

namespace BlazorSync.Tests.Browser;

/// <summary>Skipped unless <c>BLAZORSYNC_POSTGRES</c> names a PostgreSQL server where the user may create databases.</summary>
public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BLAZORSYNC_POSTGRES")))
        {
            Skip = "Set BLAZORSYNC_POSTGRES to run tests against PostgreSQL.";
        }
    }
}

/// <summary>
/// T24 I05 I06 I13: two server processes share one PostgreSQL database; browsers connected to different processes see
/// each other's notes (commit hints cross processes through LISTEN/NOTIFY), and data survives a server restart.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class MultiServerTests(BrowserFixture fixture) : IAsyncLifetime
{
    private string _database = string.Empty;
    private SampleServer _one = null!;
    private SampleServer _two = null!;

    public static TheoryData<string> Browsers() => new() { "chromium", "firefox", "webkit" };

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("BLAZORSYNC_POSTGRES") is not { Length: > 0 } admin)
        {
            return;
        }

        _database = $"bs_multi_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {_database}", connection);
            await create.ExecuteNonQueryAsync();
        }

        var environment = new Dictionary<string, string>
        {
            ["BlazorSync__PostgreSql"] = new NpgsqlConnectionStringBuilder(admin) { Database = _database }.ToString(),
        };
        _one = new SampleServer("notes-sample", "BlazorSync.Samples.Notes.Server.dll", environment);
        _two = new SampleServer("notes-sample", "BlazorSync.Samples.Notes.Server.dll", environment);
        await _one.StartAsync();
        await _two.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database.Length == 0)
        {
            return;
        }

        await _one.DisposeAsync();
        await _two.DisposeAsync();
        await using var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("BLAZORSYNC_POSTGRES"));
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_database} WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    private static async Task<IPage> OpenAsync(IBrowserContext context, Uri address)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(address.ToString());
        await Expect(page.GetByTestId("status")).Not.ToHaveTextAsync("Starting", new() { Timeout = 60_000 });
        return page;
    }

    private static async Task AddNoteAsync(IPage page, string text)
    {
        await page.GetByTestId("new-text").FillAsync(text);
        await page.GetByTestId("add").ClickAsync();
        await Expect(page.GetByTestId("note-text").Filter(new() { HasText = text })).ToBeVisibleAsync();
    }

    [PostgresTheory(DisplayName = "T24 I06 I13: devices on different server processes share notes through PostgreSQL; a server restart loses nothing")]
    [MemberData(nameof(Browsers))]
    public async Task TwoProcessesOneDatabase(string browser)
    {
        var laptop = await (await fixture.BrowserAsync(browser)).NewContextAsync();
        var phone = await (await fixture.BrowserAsync(browser)).NewContextAsync();
        await using var _ = laptop;
        await using var __ = phone;
        var onLaptop = await OpenAsync(laptop, _one.Address);
        var onPhone = await OpenAsync(phone, _two.Address);

        await AddNoteAsync(onLaptop, $"from process one ({browser})");
        await Expect(onPhone.GetByTestId("note-text").Filter(new() { HasText = $"from process one ({browser})" })).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await AddNoteAsync(onPhone, $"from process two ({browser})");
        await Expect(onLaptop.GetByTestId("note-text").Filter(new() { HasText = $"from process two ({browser})" })).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Restart process one: the data is in PostgreSQL, the epoch is unchanged, so nothing resets or disappears.
        await _one.StopAsync();
        await AddNoteAsync(onLaptop, $"while process one was down ({browser})");
        await _one.StartAsync();
        await Expect(onLaptop.GetByTestId("pending")).ToHaveTextAsync("0", new() { Timeout = 60_000 });
        await Expect(onPhone.GetByTestId("note-text").Filter(new() { HasText = $"while process one was down ({browser})" })).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(onLaptop.GetByTestId("note-text")).ToHaveCountAsync(3);
        await Expect(onPhone.GetByTestId("note-text")).ToHaveCountAsync(3);
    }
}
