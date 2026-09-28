using System.Diagnostics;
using Bsync.Diagnostics;
using Bsync.Tests.Http;
using Bsync.Tests.TestSupport;
using OpenTelemetry;
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
