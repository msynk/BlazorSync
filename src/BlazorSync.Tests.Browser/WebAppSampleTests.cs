using Microsoft.Playwright;
using Xunit;

namespace BlazorSync.Tests.Browser;

/// <summary>
/// Phase 6 (ADR-007): one component in static SSR, Interactive Server, Interactive WebAssembly and Auto, driven
/// against the published Blazor Web App sample running as its own process.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class WebAppSampleTests(BrowserFixture fixture) : IAsyncLifetime
{
    private readonly SampleServer _server = new("webapp-sample", "BlazorSync.Samples.WebApp.dll");

    public static TheoryData<string> Browsers() => new() { "chromium", "firefox", "webkit" };

    public Task InitializeAsync() => _server.StartAsync();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    private async Task<IBrowserContext> ContextAsync(string browser, bool javaScript = true) =>
        await (await fixture.BrowserAsync(browser)).NewContextAsync(new BrowserNewContextOptions { JavaScriptEnabled = javaScript });

    private async Task<IPage> OpenInteractiveAsync(IBrowserContext context, string path, string expectedRenderer)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(_server.Address, path).ToString());
        await Expect(page.GetByTestId("renderer")).ToHaveTextAsync(expectedRenderer, new() { Timeout = 60_000 });
        await Expect(page.GetByTestId("status")).Not.ToHaveTextAsync("Starting", new() { Timeout = 30_000 });
        return page;
    }

    private static async Task AddAsync(IPage page, string text)
    {
        await page.GetByTestId("new-text").FillAsync(text);
        await page.GetByTestId("add").ClickAsync();
        await Expect(page.GetByTestId("note-text").Filter(new() { HasText = text })).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    private static ILocator Note(IPage page, string text) => page.GetByTestId("note-text").Filter(new() { HasText = text });

    [Theory(DisplayName = "T46 I18: static SSR lists and adds notes with JavaScript disabled")]
    [MemberData(nameof(Browsers))]
    public async Task StaticSsrWithoutJavaScript(string browser)
    {
        await using var context = await ContextAsync(browser, javaScript: false);
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(_server.Address, "ssr").ToString());
        await Expect(page.GetByTestId("host")).ToHaveTextAsync("server");

        await page.GetByTestId("new-text").FillAsync($"ssr {browser}");
        await page.GetByTestId("add").ClickAsync();

        await Expect(page.GetByTestId("last-result")).ToHaveTextAsync("AcceptedByServer");
        await Expect(Note(page, $"ssr {browser}")).ToBeVisibleAsync();
    }

    [Theory(DisplayName = "T46: interactive pages are prerendered from server data and readable without JavaScript")]
    [MemberData(nameof(Browsers))]
    public async Task PrerenderWithoutJavaScript(string browser)
    {
        await using (var writer = await ContextAsync(browser))
        {
            await AddAsync(await OpenInteractiveAsync(writer, "server", "Server"), $"prerendered {browser}");
        }

        await using var context = await ContextAsync(browser, javaScript: false);
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(_server.Address, "wasm").ToString());

        await Expect(page.GetByTestId("renderer")).ToHaveTextAsync("Static");
        await Expect(page.GetByTestId("host")).ToHaveTextAsync("server");
        await Expect(Note(page, $"prerendered {browser}")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("add")).ToBeDisabledAsync();
    }

    [Theory(DisplayName = "T49 I13 I18: Interactive Server users see each other's changes live and after circuit recreation")]
    [MemberData(nameof(Browsers))]
    public async Task InteractiveServerLiveUpdates(string browser)
    {
        await using var alice = await ContextAsync(browser);
        await using var bob = await ContextAsync(browser);
        var alicePage = await OpenInteractiveAsync(alice, "server", "Server");
        var bobPage = await OpenInteractiveAsync(bob, "server", "Server");
        await Expect(alicePage.GetByTestId("host")).ToHaveTextAsync("server");

        await AddAsync(alicePage, $"live {browser}");
        await Expect(alicePage.GetByTestId("last-result")).ToHaveTextAsync("AcceptedByServer");
        await Expect(Note(bobPage, $"live {browser}")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await bobPage.ReloadAsync(); // a new circuit
        await Expect(bobPage.GetByTestId("renderer")).ToHaveTextAsync("Server", new() { Timeout = 30_000 });
        await Expect(Note(bobPage, $"live {browser}")).ToBeVisibleAsync();
    }

    [Theory(DisplayName = "I16 I18 I20: a WebAssembly replica saves locally, uploads, and Server users see the change")]
    [MemberData(nameof(Browsers))]
    public async Task WebAssemblyWritesReachServerUsers(string browser)
    {
        await using var device = await ContextAsync(browser);
        await using var office = await ContextAsync(browser);
        var wasm = await OpenInteractiveAsync(device, "wasm", "WebAssembly");
        var server = await OpenInteractiveAsync(office, "server", "Server");
        await Expect(wasm.GetByTestId("host")).ToHaveTextAsync("browser");

        await AddAsync(wasm, $"from wasm {browser}");
        await Expect(wasm.GetByTestId("last-result")).ToHaveTextAsync("SavedLocally");
        await Expect(wasm.GetByTestId("pending")).ToHaveTextAsync("0", new() { Timeout = 20_000 });
        await Expect(Note(server, $"from wasm {browser}")).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Theory(DisplayName = "I13: a server hint brings another user's change into a WebAssembly tab well before the 60 s interval")]
    [MemberData(nameof(Browsers))]
    public async Task HintsReachWebAssembly(string browser)
    {
        await using var device = await ContextAsync(browser);
        await using var office = await ContextAsync(browser);
        var wasm = await OpenInteractiveAsync(device, "wasm", "WebAssembly");
        await Expect(wasm.GetByTestId("status")).ToHaveTextAsync("Synced", new() { Timeout = 30_000 });
        var server = await OpenInteractiveAsync(office, "server", "Server");

        await AddAsync(server, $"hinted {browser}");

        await Expect(Note(wasm, $"hinted {browser}")).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Theory(DisplayName = "T52 I16: offline edits show as not synced and upload as soon as the browser reports it is online")]
    [MemberData(nameof(Browsers))]
    public async Task OnlineEventUploadsImmediately(string browser)
    {
        await using var device = await ContextAsync(browser);
        var wasm = await OpenInteractiveAsync(device, "wasm", "WebAssembly");
        await Expect(wasm.GetByTestId("status")).ToHaveTextAsync("Synced", new() { Timeout = 30_000 });

        await device.SetOfflineAsync(true);
        await AddAsync(wasm, $"queued {browser}");
        await Expect(wasm.GetByTestId("note-status")).ToHaveTextAsync("(not synced yet)");
        await Expect(wasm.GetByTestId("status")).ToContainTextAsync("Offline", new() { Timeout = 20_000 });
        await Task.Delay(TimeSpan.FromSeconds(6)); // let the backoff grow beyond the time allowed below

        await device.SetOfflineAsync(false);

        await Expect(wasm.GetByTestId("pending")).ToHaveTextAsync("0", new() { Timeout = 4_000 });
        await Expect(wasm.GetByTestId("note-status")).ToHaveCountAsync(0);
    }

    [Theory(DisplayName = "T47: Auto runs on the server on the first visit and in WebAssembly once the runtime is cached")]
    [MemberData(nameof(Browsers))]
    public async Task AutoFirstAndLaterVisit(string browser)
    {
        await using var context = await ContextAsync(browser);
        var first = await OpenInteractiveAsync(context, "auto", "Server");
        await Expect(first.GetByTestId("host")).ToHaveTextAsync("server");
        await AddAsync(first, $"auto server phase {browser}");

        // Load the WebAssembly runtime once, then visit the Auto page again.
        await OpenInteractiveAsync(context, "wasm", "WebAssembly");
        var later = await OpenInteractiveAsync(context, "auto", "WebAssembly");

        await Expect(later.GetByTestId("host")).ToHaveTextAsync("browser");
        await Expect(Note(later, $"auto server phase {browser}")).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Theory(DisplayName = "T48 I02 I03: the prerendered server snapshot never replaces unsynced local edits")]
    [MemberData(nameof(Browsers))]
    public async Task PrerenderDoesNotOverwriteLocalEdits(string browser)
    {
        await using var context = await ContextAsync(browser);
        var page = await OpenInteractiveAsync(context, "wasm", "WebAssembly");
        await context.RouteAsync("**/sync/**", route => route.AbortAsync());
        await AddAsync(page, $"unsynced {browser}");
        await Expect(page.GetByTestId("pending")).ToHaveTextAsync("1");

        await page.ReloadAsync();

        // The prerendered HTML comes from the server, which has not seen the note ...
        await Expect(page.GetByTestId("renderer")).ToHaveTextAsync("WebAssembly", new() { Timeout = 60_000 });

        // ... and once the browser replica takes over, the unsynced note is still there and still pending.
        await Expect(Note(page, $"unsynced {browser}")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(page.GetByTestId("pending")).ToHaveTextAsync("1");

        await context.UnrouteAllAsync();
        await Expect(page.GetByTestId("pending")).ToHaveTextAsync("0", new() { Timeout = 30_000 });
    }
}
