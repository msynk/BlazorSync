using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorSync.Clocks;
using BlazorSync.Documents;
using BlazorSync.Protocol;
using BlazorSync.Storage;
using BlazorSync.Storage.IndexedDb;
using BlazorSync.Testing;
using BlazorSync.Transport.Http;
using Microsoft.JSInterop;

namespace BlazorSync.Tests.BrowserHost;

/// <summary>
/// Entry points the Playwright tests call with <c>DotNet.invokeMethodAsync("BlazorSync.Tests.BrowserHost", ...)</c>.
/// Every method returns JSON text so no reflection-based serialization is needed in the trimmed app.
/// </summary>
public static class Harness
{
    private const string Assembly = "BlazorSync.Tests.BrowserHost";

    private static IJSRuntime _js = null!;
    private static Uri _baseAddress = null!;
    private static Replica? _replica;
    private static IndexedDbReplicaLease? _lease;

    public static void Initialize(IJSRuntime js, Uri baseAddress)
    {
        _js = js;
        _baseAddress = baseAddress;
    }

    [JSInvokable(nameof(RunStoreConformance))]
    public static async Task<string> RunStoreConformance(string databasePrefix)
    {
        var results = new List<CaseResult>();
        var n = 0;
        foreach (var conformanceCase in LocalStoreConformance.Cases)
        {
            var opened = new List<IndexedDbLocalStore<ConformanceDocument>>();
            var database = $"{databasePrefix}-{n++}";
            try
            {
                await conformanceCase.RunAsync(async () =>
                {
                    var store = await IndexedDbLocalStore<ConformanceDocument>.OpenAsync(
                        _js,
                        new IndexedDbStoreOptions { DatabaseName = database, Collection = "conformance" },
                        ConformanceJsonContext.Default.ConformanceDocument);
                    opened.Add(store);
                    return store;
                });
                results.Add(new CaseResult(conformanceCase.Name, true, null));
            }
            catch (Exception error)
            {
                results.Add(new CaseResult(conformanceCase.Name, false, $"{error.GetType().Name}: {error.Message}"));
            }
            finally
            {
                foreach (var store in opened)
                {
                    await store.DisposeAsync();
                }

                await IndexedDbLocalStore<ConformanceDocument>.DeleteDatabaseAsync(_js, database);
            }
        }

        return JsonSerializer.Serialize(results, HarnessJson.Default.ListCaseResult);
    }

    [JSInvokable(nameof(OpenReplica))]
    public static async Task<string> OpenReplica(string database, string node)
    {
        try
        {
            if (_replica is not null)
            {
                await _replica.Store.DisposeAsync();
            }

            var store = await IndexedDbLocalStore<ConformanceDocument>.OpenAsync(
                _js,
                new IndexedDbStoreOptions { DatabaseName = database, Collection = "notes" },
                ConformanceJsonContext.Default.ConformanceDocument);
            var http = new HttpClient { BaseAddress = _baseAddress };
            var transport = new HttpSyncTransport<ConformanceDocument>(
                http,
                new HttpSyncTransportOptions { Collection = "notes", SchemaId = "notes-v1", RequestTimeout = TimeSpan.FromSeconds(10) },
                SyncJsonTypes<ConformanceDocument>.From(ConformanceJsonContext.Default));
            var engine = new SyncEngine<ConformanceDocument>(
                store,
                transport,
                new HybridLogicalClock(node),
                DocumentCloner.Json(ConformanceJsonContext.Default.ConformanceDocument));
            _replica = new Replica(store, engine);
            return Ok();
        }
        catch (Exception error)
        {
            return Error(error);
        }
    }

    [JSInvokable(nameof(Write))]
    public static Task<string> Write(string id, string title) =>
        Guard(async replica => (await replica.Engine.WriteAsync(new ConformanceDocument { Id = id, Title = title })).LocalRevision.ToString());

    [JSInvokable(nameof(Delete))]
    public static Task<string> Delete(string id) =>
        Guard(async replica => (await replica.Engine.DeleteAsync(id))?.LocalRevision.ToString() ?? "none");

    [JSInvokable(nameof(Sync))]
    public static Task<string> Sync() =>
        Guard(async replica =>
        {
            var result = await replica.Engine.SyncAsync();
            return JsonSerializer.Serialize(
                new SyncSummary(result.Pulled, result.Pushed, result.Conflicts, result.IsComplete, result.ResetPerformed, result.MissingAfterReset),
                HarnessJson.Default.SyncSummary);
        });

    [JSInvokable(nameof(Query))]
    public static Task<string> Query() =>
        Guard(async replica =>
        {
            var documents = await replica.Engine.QueryAsync(includeDeleted: true);
            var views = new List<DocumentView>();
            foreach (var document in documents.OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                var record = await replica.Engine.GetAsync(document.Id);
                views.Add(new DocumentView(document.Id, document.Title, document.Deleted, record!.IsDirty));
            }

            return JsonSerializer.Serialize(views, HarnessJson.Default.ListDocumentView);
        });

    [JSInvokable(nameof(Identity))]
    public static Task<string> Identity() =>
        Guard(async replica => (await replica.Store.GetReplicaIdentityAsync()).ReplicaId);

    [JSInvokable(nameof(CloseReplica))]
    public static async Task<string> CloseReplica()
    {
        if (_replica is not null)
        {
            await _replica.Store.DisposeAsync();
            _replica = null;
        }

        return Ok();
    }

    [JSInvokable(nameof(TryLease))]
    public static async Task<string> TryLease(string name)
    {
        try
        {
            _lease = await IndexedDbReplicaLease.TryAcquireAsync(_js, name);
            return _lease is null ? "busy" : "acquired";
        }
        catch (Exception error)
        {
            return Error(error);
        }
    }

    [JSInvokable(nameof(ReleaseLease))]
    public static async Task<string> ReleaseLease()
    {
        if (_lease is not null)
        {
            await _lease.DisposeAsync();
            _lease = null;
        }

        return Ok();
    }

    [JSInvokable(nameof(DeleteDatabase))]
    public static async Task<string> DeleteDatabase(string name)
    {
        try
        {
            await IndexedDbLocalStore<ConformanceDocument>.DeleteDatabaseAsync(_js, name);
            return Ok();
        }
        catch (Exception error)
        {
            return Error(error);
        }
    }

    private static async Task<string> Guard(Func<Replica, Task<string>> action)
    {
        if (_replica is null)
        {
            return Error(new InvalidOperationException("No replica is open."));
        }

        try
        {
            return await action(_replica);
        }
        catch (Exception error)
        {
            return Error(error);
        }
    }

    private static string Ok() => "ok";

    private static string Error(Exception error) => error switch
    {
        LocalStoreUnavailableException store => $"error:store:{store.Reason}",
        SyncTransportException transport => $"error:transport:{transport.ErrorCode}",
        _ => $"error:{error.GetType().Name}:{error.Message}",
    };

    private sealed record Replica(IndexedDbLocalStore<ConformanceDocument> Store, SyncEngine<ConformanceDocument> Engine);
}

public sealed record CaseResult(string Name, bool Passed, string? Error);

public sealed record SyncSummary(int Pulled, int Pushed, int Conflicts, bool Complete, bool Reset, int Missing);

public sealed record DocumentView(string Id, string Title, bool Deleted, bool Dirty);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<CaseResult>))]
[JsonSerializable(typeof(SyncSummary))]
[JsonSerializable(typeof(List<DocumentView>))]
internal sealed partial class HarnessJson : JsonSerializerContext;
