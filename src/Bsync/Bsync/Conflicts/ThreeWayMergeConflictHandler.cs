using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Bsync.Conflicts;

/// <summary>
/// Merges conflicting edits field by field (<see cref="ThreeWayMerge"/>). Changes to different fields are combined
/// and pushed; if both sides changed the same field, or one deleted what the other changed, or the common ancestor
/// is unknown, the decision goes to <paramref name="fallback"/> (by default <see cref="DeferConflictHandler{TDocument}"/>,
/// which keeps the conflict for the user). Deterministic and free of side effects.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="typeInfo">Source-generated JSON metadata for the document type.</param>
/// <param name="fallback">The handler for conflicts the merge cannot settle.</param>
/// <param name="options">Members merged as sets or counters.</param>
public sealed class ThreeWayMergeConflictHandler<TDocument>(JsonTypeInfo<TDocument> typeInfo, IConflictHandler<TDocument>? fallback = null, ThreeWayMergeOptions? options = null) : IConflictHandler<TDocument>
    where TDocument : class, ISyncEntity
{
    private readonly JsonTypeInfo<TDocument> _typeInfo = typeInfo ?? throw new ArgumentNullException(nameof(typeInfo));
    private readonly IConflictHandler<TDocument> _fallback = fallback ?? new DeferConflictHandler<TDocument>();

    /// <inheritdoc />
    public ConflictResolution<TDocument> Resolve(ConflictContext<TDocument> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.AssumedMaster is null)
        {
            return _fallback.Resolve(context);
        }

        var result = ThreeWayMerge.Merge(context.AssumedMaster, context.Fork, context.RealMaster, _typeInfo, options ?? ThreeWayMergeOptions.Default);
        if (!result.IsClean)
        {
            return _fallback.Resolve(context);
        }

        return result.SameAsServer ? ConflictResolution<TDocument>.AcceptMaster() : ConflictResolution<TDocument>.Resolve(result.Merged);
    }
}
