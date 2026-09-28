using Bsync.Protocol;
using Bsync.Transport;

namespace Bsync.Tests.TestSupport;

/// <summary>
/// Serializes every request and response through the wire JSON encoding (source-generated metadata),
/// so the engine only ever sees what a real network peer would have sent.
/// </summary>
public sealed class JsonWireTransport<T>(ISyncTransport<T> inner, System.Text.Json.Serialization.JsonSerializerContext context) : ISyncTransport<T>
    where T : class, ISyncEntity
{
    public async Task<PullResult<T>> PullAsync(PullRequest request, CancellationToken cancellationToken = default)
    {
        var result = await inner.PullAsync(RoundTrip(request), cancellationToken);
        return RoundTrip(result);
    }

    public async Task<PushResult<T>> PushAsync(PushRequest<T> request, CancellationToken cancellationToken = default)
    {
        var result = await inner.PushAsync(RoundTrip(request), cancellationToken);
        return RoundTrip(result);
    }

    public IAsyncEnumerable<StreamEvent<T>> StreamAsync(Checkpoint since, CancellationToken cancellationToken = default) =>
        inner.StreamAsync(since, cancellationToken);

    private TValue RoundTrip<TValue>(TValue value)
    {
        var typeInfo = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue>)context.GetTypeInfo(typeof(TValue))!;
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        return System.Text.Json.JsonSerializer.Deserialize(bytes, typeInfo)!;
    }
}
