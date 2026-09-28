using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Bsync.Tests.Browser;

/// <summary>Runs a published sample server as a separate process on a free localhost port.</summary>
public sealed class SampleServer : IAsyncDisposable
{
    private readonly string _publishDir;
    private readonly string _assembly;
    private Process? _process;
    private readonly IReadOnlyDictionary<string, string>? _environment;

    public SampleServer(string publishFolder, string assembly, IReadOnlyDictionary<string, string>? environment = null)
    {
        _environment = environment;
        _publishDir = Path.Combine(AppContext.BaseDirectory, publishFolder);
        _assembly = assembly;
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        Address = new Uri($"http://127.0.0.1:{((IPEndPoint)probe.LocalEndpoint).Port}/");
        probe.Stop();
    }

    public Uri Address { get; }

    public string PublishDir => _publishDir;

    public async Task StartAsync()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _publishDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var (name, value) in _environment ?? new Dictionary<string, string>())
        {
            start.Environment[name] = value;
        }

        start.ArgumentList.Add(Path.Combine(_publishDir, _assembly));
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add(Address.ToString().TrimEnd('/'));
        _process = Process.Start(start)!;
        _process.OutputDataReceived += (_, _) => { };
        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using var http = new HttpClient();
        for (var attempt = 0; attempt < 150; attempt++)
        {
            try
            {
                if ((await http.GetAsync(Address)).IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException($"{_assembly} did not start.");
    }

    public async Task StopAsync()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process?.Dispose();
        _process = null;
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
