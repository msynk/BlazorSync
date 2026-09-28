using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace BlazorSync.Protocol;

/// <summary>
/// The source-generated JSON metadata a transport or endpoint needs for one document type. Build it from the
/// application's <see cref="JsonSerializerContext"/>, which must declare <c>[JsonSerializable]</c> for
/// <see cref="PullRequest"/>, <see cref="PullResult{TDocument}"/>, <see cref="PushRequest{TDocument}"/> and
/// <see cref="PushResult{TDocument}"/>.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed record SyncJsonTypes<TDocument>(
    JsonTypeInfo<PullRequest> PullRequest,
    JsonTypeInfo<PullResult<TDocument>> PullResult,
    JsonTypeInfo<PushRequest<TDocument>> PushRequest,
    JsonTypeInfo<PushResult<TDocument>> PushResult)
    where TDocument : class, ISyncEntity
{
    /// <summary>Resolves all four types from <paramref name="context"/>.</summary>
    /// <exception cref="InvalidOperationException">A type is missing from the context.</exception>
    public static SyncJsonTypes<TDocument> From(JsonSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new SyncJsonTypes<TDocument>(
            Get<PullRequest>(context),
            Get<PullResult<TDocument>>(context),
            Get<PushRequest<TDocument>>(context),
            Get<PushResult<TDocument>>(context));
    }

    private static JsonTypeInfo<T> Get<T>(JsonSerializerContext context) =>
        context.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
        ?? throw new InvalidOperationException(
            $"{context.GetType().Name} has no metadata for {typeof(T).Name}; add [JsonSerializable(typeof(...))] for the four BlazorSync protocol types.");
}
