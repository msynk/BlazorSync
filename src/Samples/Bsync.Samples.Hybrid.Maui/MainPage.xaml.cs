using System.Text.Json;
using Microsoft.AspNetCore.Components.WebView;

namespace Bsync.Samples.Hybrid.Maui;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        if (MauiProgram.Settings.Smoke is { } result)
        {
            blazorWebView.BlazorWebViewInitialized += (_, e) => _ = Dispatcher.DispatchAsync(() => RunSmokeAsync(e, result));
        }
    }

    /// <summary>Automated check used by the tests: adds a note through the real UI, waits until it is uploaded, and quits.</summary>
    private static async Task RunSmokeAsync(BlazorWebViewInitializedEventArgs e, string resultFile)
    {
        string outcome;
        try
        {
#if WINDOWS
            var web = e.WebView.CoreWebView2;
            async Task<string> Eval(string script) => JsonSerializer.Deserialize<string>(await web.ExecuteScriptAsync(script)) ?? string.Empty;
            async Task Until(string condition, string what)
            {
                for (var i = 0; i < 600; i++)
                {
                    if (await Eval($"String(!!({condition}))") == "true")
                    {
                        return;
                    }

                    await Task.Delay(100);
                }

                throw new TimeoutException($"Timed out waiting for {what}. Page: {await Eval("document.body.innerText")}");
            }

            await Until("document.querySelector('[data-testid=notes-panel]') && !document.querySelector('[data-testid=new-text]').disabled", "the notes panel");
            var text = MauiProgram.Settings.Text ?? $"from MAUI {Guid.NewGuid():N}";
            await Eval($"(() => {{ const i = document.querySelector('[data-testid=new-text]'); i.value = '{text}'; i.dispatchEvent(new Event('input', {{ bubbles: true }})); return 'typed'; }})()");
            await Until("!document.querySelector('[data-testid=add]').disabled", "the add button");
            await Eval("document.querySelector('[data-testid=add]').click(), 'clicked'");
            await Until($"[...document.querySelectorAll('[data-testid=note-text]')].some(n => n.textContent === '{text}')", "the note");
            await Until("document.querySelector('[data-testid=pending]').textContent === '0'", "the upload");
            outcome = $"ok {text} host={await Eval("document.querySelector('[data-testid=host]').textContent")} renderer={await Eval("document.querySelector('[data-testid=renderer]').textContent")}";
#else
            outcome = "error smoke mode is implemented for Windows only";
            await Task.CompletedTask;
#endif
        }
        catch (Exception error)
        {
            outcome = $"error {error}";
        }

        await File.WriteAllTextAsync(resultFile, outcome);
        Application.Current?.Quit();
    }
}
