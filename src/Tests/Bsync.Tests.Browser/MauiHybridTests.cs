using System.Diagnostics;
using Xunit;

namespace Bsync.Tests.Browser;

/// <summary>Skipped where the MAUI sample cannot run (not Windows, or not built because the maui-windows workload is missing).</summary>
public sealed class MauiWindowsFactAttribute : FactAttribute
{
    public MauiWindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "The MAUI sample is built for Windows here.";
        }
        else if (MauiHybridTests.Executable is null)
        {
            Skip = "The MAUI sample was not built (needs the maui-windows workload).";
        }
    }
}

/// <summary>ADR-007 native host: the .NET MAUI Blazor Hybrid sample on Windows writes a note through its real UI and uploads it.</summary>
public sealed class MauiHybridTests : IAsyncLifetime
{
    private readonly SampleServer _server = new("notes-sample", "Bsync.Samples.Notes.Server.dll");
    private readonly string _data = Path.Combine(Path.GetTempPath(), "bsync-maui", Guid.NewGuid().ToString("N"));

    public static string? Executable { get; } = Find();

    private static string? Find()
    {
        var bin = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Samples", "Bsync.Samples.Hybrid.Maui", "bin"));
        return Directory.Exists(bin)
            ? Directory.EnumerateFiles(bin, "Bsync.Samples.Hybrid.Maui.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
    }

    public Task InitializeAsync() => _server.StartAsync();

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [MauiWindowsFact(DisplayName = "ADR-007 I16: the MAUI Hybrid app (Windows) writes through its UI and uploads the note")]
    public async Task MauiAppSyncs()
    {
        Directory.CreateDirectory(_data);
        var result = Path.Combine(_data, "result.txt");
        var start = new ProcessStartInfo(Executable!) { UseShellExecute = false };
        foreach (var argument in new[] { "--server", _server.Address.ToString(), "--data", Path.Combine(_data, "replica"), "--smoke", result, "--text", "written in MAUI" })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        var outcome = File.Exists(result) ? await File.ReadAllTextAsync(result) : "no result";
        Assert.StartsWith("ok written in MAUI host=native renderer=WebView", outcome);

        using var http = new HttpClient { BaseAddress = _server.Address };
        using var request = new HttpRequestMessage(HttpMethod.Post, "sync/collections/notes/pull")
        {
            Content = new StringContent("""{"checkpoint":null,"limit":100}""", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Bsync-Protocol", "1");
        request.Headers.Add("Bsync-Schema", "notes-v1");
        Assert.Contains("written in MAUI", await (await http.SendAsync(request)).Content.ReadAsStringAsync());
    }
}
