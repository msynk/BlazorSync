using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using BlazorSync.Diagnostics;
using BlazorSync.Protocol;
using BlazorSync.Tests.TestSupport;
using Xunit;

namespace BlazorSync.Tests;

/// <summary>Phase 10: traces and metrics (docs/operations/observability.md). Each test filters by a unique engine name.</summary>
public sealed class DiagnosticsTests
{
    private sealed class Recorder : IDisposable
    {
        private readonly MeterListener _meters = new();
        private readonly ActivityListener _activities;
        private readonly string _name;

        public Recorder(string name)
        {
            _name = name;
            _meters.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == SyncDiagnostics.SourceName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _meters.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
            _meters.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
            _meters.Start();
            _activities = new ActivityListener
            {
                ShouldListenTo = source => source.Name == SyncDiagnostics.SourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (Equals(activity.GetTagItem(SyncDiagnostics.NameTag), _name))
                    {
                        Activities.Add(activity);
                    }
                },
            };
            ActivitySource.AddActivityListener(_activities);
        }

        public ConcurrentQueue<(string Instrument, double Value, Dictionary<string, object?> Tags)> Measurements { get; } = new();

        public ConcurrentBag<Activity> Activities { get; } = [];

        public void Collect() => _meters.RecordObservableInstruments();

        public double Sum(string instrument, string? tag = null, object? value = null) =>
            Measurements.Where(m => m.Instrument == instrument && (tag is null || Equals(m.Tags.GetValueOrDefault(tag), value))).Sum(m => m.Value);

        public double Last(string instrument) => Measurements.Last(m => m.Instrument == instrument).Value;

        private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }

            if (Equals(copy.GetValueOrDefault(SyncDiagnostics.NameTag), _name))
            {
                Measurements.Enqueue((instrument.Name, value, copy));
            }
        }

        public void Dispose()
        {
            _meters.Dispose();
            _activities.Dispose();
        }
    }

    private static TestReplica Replica(InMemorySyncServerRef server, string node, string name, Conflicts.IConflictHandler<Note>? handler = null) =>
        new(server, node, handler, new SyncOptions<Note> { DiagnosticsName = name });

    [Fact(DisplayName = "Operations, pulls, conflicts, run durations and spans are recorded with outcome tags and no document data")]
    public async Task RunsAreRecorded()
    {
        var name = $"diag-{Guid.NewGuid():N}";
        using var recorder = new Recorder(name);
        var server = new InMemorySyncServerRef(new Server.InMemorySyncServer<Note>(NoteJson.ServerOptions(validator: (op, _) => op.Document.Title == "bad" ? PushErrorCodes.Forbidden : null)));
        var other = new TestReplica(server, "other");
        var client = Replica(server, "client", name);
        await other.Engine.WriteAsync(new Note { Id = "shared", Title = "base" });
        await other.Engine.SyncAsync();
        await client.Engine.SyncAsync();
        await other.Engine.WriteAsync(new Note { Id = "shared", Title = "theirs" });
        await other.Engine.SyncAsync();

        await client.Engine.WriteAsync(new Note { Id = "shared", Title = "mine" });
        await client.Engine.WriteAsync(new Note { Id = "ok", Title = "fine" });
        await client.Engine.WriteAsync(new Note { Id = "no", Title = "bad" });
        await client.Engine.PushAsync();

        Assert.Equal(1, recorder.Sum("blazorsync.push.operations", "blazorsync.outcome", "accepted"));
        Assert.Equal(1, recorder.Sum("blazorsync.push.operations", "blazorsync.outcome", "conflict"));
        Assert.Equal(1, recorder.Sum("blazorsync.push.operations", "error.type", PushErrorCodes.Forbidden));
        Assert.Equal(1, recorder.Sum("blazorsync.conflicts", "blazorsync.decision", "defer"));
        Assert.Equal(1, recorder.Sum("blazorsync.pull.changes"));
        Assert.Equal(2, recorder.Measurements.Count(m => m.Instrument == "blazorsync.run.duration"));

        var push = Assert.Single(recorder.Activities, a => a.OperationName == "blazorsync.push");
        Assert.Equal((1, 1, 1), ((int)push.GetTagItem("blazorsync.pushed")!, (int)push.GetTagItem("blazorsync.conflicts")!, (int)push.GetTagItem("blazorsync.rejected")!));
        Assert.Single(recorder.Activities, a => a.OperationName == "blazorsync.sync");

        // Tags never carry document ids, titles or bodies.
        var values = recorder.Measurements.SelectMany(m => m.Tags.Values).Concat(recorder.Activities.SelectMany(a => a.TagObjects.Select(t => t.Value))).Select(v => v?.ToString()).ToList();
        Assert.DoesNotContain(values, v => v is "shared" or "ok" or "no" or "mine" or "fine" or "bad" or "theirs");
    }

    [Fact(DisplayName = "Queue depth and oldest pending age are observable; a failed run is tagged with its error code")]
    public async Task QueueGaugesAndErrors()
    {
        var name = $"diag-{Guid.NewGuid():N}";
        using var recorder = new Recorder(name);
        var clock = new ManualClock(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 60_000);
        var client = new TestReplica(InMemorySyncServerRef.Create(clock), "client", options: new SyncOptions<Note> { DiagnosticsName = name }, physicalClock: clock);
        await client.Engine.WriteAsync(new Note { Id = "a" });
        await client.Engine.WriteAsync(new Note { Id = "b" });
        client.Transport.FailBeforeSend = true;
        await Assert.ThrowsAsync<InjectedFaultException>(() => client.Engine.PushAsync());

        recorder.Collect();
        Assert.Equal(2, recorder.Last("blazorsync.queue.depth"));
        Assert.InRange(recorder.Last("blazorsync.queue.oldest_age"), 55, 600);
        var failed = Assert.Single(recorder.Activities);
        Assert.Equal((ActivityStatusCode.Error, nameof(InjectedFaultException)), (failed.Status, failed.GetTagItem("error.type")));
        Assert.Equal(1, recorder.Measurements.Count(m => m.Instrument == "blazorsync.run.duration" && Equals(m.Tags["blazorsync.result"], "error")));

        await client.Engine.SyncAsync();
        recorder.Collect();
        Assert.Equal((0, 0), (recorder.Last("blazorsync.queue.depth"), recorder.Last("blazorsync.queue.oldest_age")));
    }

    [Fact(DisplayName = "Resets are counted by reason")]
    public async Task ResetsAreCounted()
    {
        var name = $"diag-{Guid.NewGuid():N}";
        using var recorder = new Recorder(name);
        var server = InMemorySyncServerRef.Create();
        var client = Replica(server, "client", name);
        await client.Engine.WriteAsync(new Note { Id = "n1" });
        await client.Engine.SyncAsync();
        server.Restore(server.Server.CreateBackup());

        await client.Engine.SyncAsync();

        Assert.Equal(1, recorder.Sum("blazorsync.resets", "blazorsync.reason", ResetReasons.Epoch));
        Assert.Contains(recorder.Activities.Single(a => a.OperationName == "blazorsync.sync" && a.Events.Any()).Events, e => e.Name == "blazorsync.reset");
    }

    [Fact(DisplayName = "An empty diagnostics name is refused")]
    public void NameValidated() =>
        Assert.ThrowsAny<ArgumentException>(() => new SyncOptions<Note> { DiagnosticsName = " " }.Validate());
}
