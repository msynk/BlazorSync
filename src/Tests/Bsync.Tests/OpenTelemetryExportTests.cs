using System.Diagnostics;
using Bsync.Diagnostics;
using Bsync.Server.AspNetCore;
using Bsync.Tests.Http;
using Bsync.Tests.TestSupport;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace Bsync.Tests;

/// <summary>A thread-safe exporter keeping this test's spans: the provider sees spans of tests running in parallel too.</summary>
internal sealed class SpanSink(string name) : BaseExporter<Activity>
{
    public System.Collections.Concurrent.ConcurrentQueue<Activity> Exported { get; } = new();

    public override ExportResult Export(in Batch<Activity> batch)
    {
        foreach (var activity in batch)
        {
            if (Equals(activity.GetTagItem(SyncDiagnostics.NameTag), name))
            {
                Exported.Enqueue(activity);
            }
        }

        return ExportResult.Success;
    }
}

/// <summary>Phase 10: the documented OpenTelemetry wiring exports Bsync's spans and metrics through the SDK.</summary>
public sealed class OpenTelemetryExportTests
{
    [Fact(DisplayName = "The OpenTelemetry SDK exports Bsync spans, client metrics and server metrics as documented")]
    public async Task SdkExports()
    {
        var name = $"otel-{Guid.NewGuid():N}";
        var spans = new SpanSink(name);
        var metrics = new List<Metric>();
        using var tracing = Sdk.CreateTracerProviderBuilder()
            .AddSource(SyncDiagnostics.SourceName)
            .SetSampler(new AlwaysOnSampler())
            .AddProcessor(new SimpleActivityExportProcessor(spans))
            .Build();
        using var meters = Sdk.CreateMeterProviderBuilder()
            .AddMeter(SyncDiagnostics.SourceName, SyncEndpoints.MeterName)
            .AddInMemoryExporter(metrics)
            .Build();

        await using var host = await SyncTestHost.StartAsync(new RefAuthority(InMemorySyncServerRef.Create(Clocks.SystemPhysicalClock.Instance)));
        var engine = new SyncEngine<Note>(
            new Storage.InMemoryLocalStore<Note>(NoteJson.Clone),
            host.Transport(),
            new Clocks.HybridLogicalClock("otel"),
            NoteJson.Clone,
            options: new SyncOptions<Note> { DiagnosticsName = name });
        await engine.WriteAsync(new Note { Id = "n1" });
        await engine.SyncAsync();

        tracing.ForceFlush();
        meters.ForceFlush();

        var sync = Assert.Single(spans.Exported, s => s.OperationName == "bsync.sync");
        Assert.Equal(1, sync.GetTagItem("bsync.pushed"));
        var names = metrics.Select(m => m.Name).ToHashSet();
        Assert.Contains("bsync.push.operations", names);
        Assert.Contains("bsync.run.duration", names);
        Assert.Contains("bsync.queue.depth", names);
        Assert.Contains("bsync.server.requests", names);
        Assert.Contains("bsync.server.push.operations", names);
    }
}
