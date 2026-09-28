# Observability

BlazorSync emits traces and metrics through the .NET built-ins (`ActivitySource`, `Meter`). It emits logs
through `ILogger` in the packages that already depend on Microsoft.Extensions: the Blazor session and the
ASP.NET Core endpoints. The core package takes no logging dependency.

Nothing records document contents, document ids or account names. Tags carry counts, outcome kinds,
error codes, collection names and the engine's diagnostics name.

## Wiring it up

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(SyncDiagnostics.SourceName))                        // "BlazorSync"
    .WithMetrics(m => m.AddMeter(SyncDiagnostics.SourceName, SyncEndpoints.MeterName)); // + "BlazorSync.Server"
```

Give each engine a low-cardinality name with `SyncOptions<T>.DiagnosticsName`, for example the collection
name. The default is the document type name.

## Client engine (`BlazorSync` meter and activity source)

| Instrument | Type | Unit | Tags | Meaning |
|---|---|---|---|---|
| `blazorsync.push.operations` | counter | operations | `blazorsync.name`, `blazorsync.outcome` (`accepted`, `conflict`, `rejected`, `retry-later`, `missing`), `blazorsync.duplicate`, `error.type` for rejections | Every operation sent and what the server answered. `missing` means the response omitted it (resent later). |
| `blazorsync.pull.changes` | counter | changes | `blazorsync.name` | Server changes applied locally. |
| `blazorsync.conflicts` | counter | conflicts | `blazorsync.name`, `blazorsync.decision` (`use-master`, `use-resolved`, `keep-fork`, `defer`) | Conflict handler decisions. |
| `blazorsync.resets` | counter | resets | `blazorsync.name`, `blazorsync.reason` (`epoch`, `scope-changed`, `expired`) | Replica resets. |
| `blazorsync.run.duration` | histogram | s | `blazorsync.name`, `blazorsync.operation` (`sync`, `pull`, `push`), `blazorsync.result` (`complete`, `incomplete`, `error`), `error.type` | Duration of replication runs. |
| `blazorsync.queue.depth` | observable gauge | documents | `blazorsync.name` | Documents with unconfirmed local changes, as of the engine's last run. |
| `blazorsync.queue.oldest_age` | observable gauge | s | `blazorsync.name` | Age of the oldest change waiting for upload, by its authoring time, as of the last run. |

The queue gauges cost two small store reads per run. They are measured only while a listener subscribes to
them.

Spans: `blazorsync.sync`, `blazorsync.pull` and `blazorsync.push`, with the tags `blazorsync.pulled`,
`pushed`, `conflicts`, `rejected`, `deferred`, `reset` and `result`. A failed run has error status and
`error.type` (the transport error code, `cancelled`, or the exception type). A reset adds a
`blazorsync.reset` event with its reason.

## Session (`BlazorSync.SyncSession` log category)

| Event | Level | When |
|---|---|---|
| `SyncStateChanged` (1) | Warning for `Offline` and `AttentionRequired`; Debug for `Syncing` and `Synced`; Information otherwise | Every state change, with the pending count and the status detail (codes only). |
| `SyncProtocolError` (2) | Error | The server sent an invalid response. |
| `SyncUnexpectedError` (3) | Error | Any other failure. The session reports `AttentionRequired`, keeps local work, and retries after the maximum backoff or when asked. |

The DI recipes (`AddLocalSyncCollection`, `AddBrowserSyncCollection`) use the container's logger factory. Set
`SyncSessionOptions.Logger` to override it.

## Server endpoints (`BlazorSync.Server` meter and log category)

| Instrument | Tags | Meaning |
|---|---|---|
| `blazorsync.server.requests` | `blazorsync.collection`, `blazorsync.endpoint` (`pull`, `push`, `hints`), `blazorsync.result` (`ok` or a problem code) | Every protocol request. |
| `blazorsync.server.push.operations` | `blazorsync.collection`, `blazorsync.outcome`, `blazorsync.duplicate` | Operations decided by the authority. |

Logs:

- `SyncRequestRefused` (1) is logged for every problem response, with collection, endpoint, status, code and
  reset reason. Its level is Information for 4xx and Warning for 5xx.
- `SyncAuthorityFailed` (2) is logged at Error, with the exception, when the authority throws unexpectedly. The
  client receives only a generic `503 unavailable`, which it retries.

ASP.NET Core's own `http.server.*` metrics and request logs cover transport-level details.

## What to alert on

- `blazorsync.queue.oldest_age` growing on devices you collect telemetry from: uploads are not getting through.
- `blazorsync.push.operations{outcome="rejected"}` by `error.type`: validation or permission problems, or
  `clock-skew` on devices with a wrong clock.
- `blazorsync.conflicts{decision="defer"}`: conflicts waiting for users.
- `blazorsync.resets` by reason, which is expected after a restore or a permission change and suspicious
  otherwise.
- `blazorsync.server.requests{result="unavailable"}` together with `SyncAuthorityFailed` logs.

## Evidence

- `DiagnosticsTests`: outcome, conflict, pull and reset counters, spans, queue gauges, error tagging, and the
  absence of document data in tags.
- `ServerObservabilityTests`: request and operation counters, refusal logs, and the retryable 503 without
  leaked details.
- `BlazorIntegrationTests.UnexpectedFailureIsReportedAndRecovers` and `RecipeUsesContainerLogging`.

Not verified: an OpenTelemetry exporter end to end (no collector here), and browser-side telemetry export.
