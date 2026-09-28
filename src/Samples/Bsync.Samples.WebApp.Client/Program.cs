using Bsync.Protocol;
using Bsync.Samples.Shared;
using Bsync.Blazor.IndexedDb;
using Bsync.Transport;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// In the browser, ISyncCollection<Note> is an IndexedDB replica synced over HTTP. The server project registers
// the server-connected implementation for Interactive Server, prerendering and static rendering instead.
var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);
builder.Services.AddBrowserSyncCollection<Note>(
    "notes",
    NotesJson.Default.Note,
    (_, _) => new HttpSyncTransport<Note>(
        new HttpClient { BaseAddress = baseAddress },
        new HttpSyncTransportOptions { Collection = "notes", SchemaId = NotesJson.SchemaId },
        SyncJsonTypes<Note>.From(NotesJson.Default)),
    // A long interval on purpose: prompt updates come from server hints and browser online/visibility events;
    // the interval is only the safety net for missed hints.
    configure: options => options with { Interval = TimeSpan.FromSeconds(60), MaxBackoff = TimeSpan.FromSeconds(10) });

await builder.Build().RunAsync();
