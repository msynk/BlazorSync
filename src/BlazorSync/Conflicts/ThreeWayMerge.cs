using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace BlazorSync.Conflicts;

/// <summary>
/// Field-level three-way merge of documents through their JSON form (ADR-006). Works with source-generated
/// metadata, so it is trimming- and AOT-safe.
/// </summary>
/// <remarks>
/// <para>Rules, applied to each JSON member recursively:</para>
/// <list type="bullet">
/// <item>If local and server agree, that value is used. If only one side changed a member relative to the base,
/// that side's value is used.</item>
/// <item>If both sides changed a member differently, it is a field conflict: the server value is kept in
/// <see cref="ThreeWayMergeResult{TDocument}.Merged"/> and the member's JSON Pointer is reported.</item>
/// <item>Objects merge member by member. An absent member differs from a member set to <c>null</c>
/// (removing a member is a change). Arrays, strings and numbers are atomic values: two different edits of one
/// array conflict even if they touched different elements, unless <see cref="ThreeWayMergeOptions"/> declares the
/// member a set (additions and removals from both sides combine) or a counter (both sides' increments add up).</item>
/// <item>Members captured by <c>[JsonExtensionData]</c> (fields unknown to this version) merge like any other
/// member, so neither side loses them.</item>
/// <item><see cref="ISyncEntity.UpdatedAt"/> is not merged (a resolution is re-stamped when it is written).
/// <see cref="ISyncEntity.Deleted"/> is merged as one value, except that a delete on one side and any content
/// change on the other is a conflict reported as <see cref="DeletionConflict"/>.</item>
/// </list>
/// </remarks>
public static class ThreeWayMerge
{
    /// <summary>The path reported when one side deleted the document and the other side changed it.</summary>
    public const string DeletionConflict = "(deleted)";

    /// <summary>Merges <paramref name="local"/> and <paramref name="server"/>, which both derive from <paramref name="base"/>.</summary>
    /// <returns>
    /// The merged document (with the server's id and timestamp) and the conflicting member paths. The inputs are not
    /// modified.
    /// </returns>
    public static ThreeWayMergeResult<TDocument> Merge<TDocument>(TDocument @base, TDocument local, TDocument server, JsonTypeInfo<TDocument> typeInfo)
        where TDocument : class, ISyncEntity =>
        Merge(@base, local, server, typeInfo, ThreeWayMergeOptions.Default);

    /// <summary>Merges with semantic rules for the members named in <paramref name="options"/>.</summary>
    public static ThreeWayMergeResult<TDocument> Merge<TDocument>(TDocument @base, TDocument local, TDocument server, JsonTypeInfo<TDocument> typeInfo, ThreeWayMergeOptions options)
        where TDocument : class, ISyncEntity
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(@base);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(typeInfo);

        var baseNode = Content(@base, typeInfo);
        var localNode = Content(local, typeInfo);
        var serverNode = Content(server, typeInfo);
        var conflicts = new List<string>();
        bool deleted;
        JsonNode? merged;
        if (local.Deleted && server.Deleted)
        {
            // Deleted on both sides: the content of a tombstone does not matter.
            (deleted, merged) = (true, serverNode);
        }
        else
        {
            merged = MergeSlot(new Slot(true, baseNode), new Slot(true, localNode), new Slot(true, serverNode), string.Empty, conflicts, options).Value;
            deleted = server.Deleted;
            if (local.Deleted != server.Deleted)
            {
                // Exactly one side changed the deletion flag; the other side must not have changed the content.
                var (deleter, otherContent) = local.Deleted != @base.Deleted ? (local, serverNode) : (server, localNode);
                if (JsonNode.DeepEquals(baseNode, otherContent))
                {
                    deleted = deleter.Deleted;
                }
                else
                {
                    conflicts.Insert(0, DeletionConflict);
                }
            }
        }

        var document = merged.Deserialize(typeInfo) ?? throw new JsonException("The merged document deserialized to null.");
        document.Id = server.Id;
        document.UpdatedAt = server.UpdatedAt;
        document.Deleted = deleted;
        var sameAsServer = deleted == server.Deleted && (deleted || JsonNode.DeepEquals(merged, serverNode));
        return new ThreeWayMergeResult<TDocument>(document, conflicts, sameAsServer);
    }

    // The document without its replication metadata, so the timestamp never conflicts.
    private static JsonNode Content<TDocument>(TDocument document, JsonTypeInfo<TDocument> typeInfo)
        where TDocument : class, ISyncEntity
    {
        var copy = JsonSerializer.SerializeToNode(document, typeInfo).Deserialize(typeInfo)!;
        copy.UpdatedAt = default;
        copy.Deleted = false;
        return JsonSerializer.SerializeToNode(copy, typeInfo)!;
    }

    private readonly record struct Slot(bool Present, JsonNode? Value);

    private static bool Same(Slot a, Slot b) => a.Present == b.Present && (!a.Present || JsonNode.DeepEquals(a.Value, b.Value));

    private static Slot MergeSlot(Slot @base, Slot local, Slot server, string path, List<string> conflicts, ThreeWayMergeOptions options)
    {
        if (Same(local, server) || Same(@base, server))
        {
            return local;
        }

        if (Same(@base, local))
        {
            return server;
        }

        if (@base.Value is JsonObject baseObject && local.Value is JsonObject localObject && server.Value is JsonObject serverObject)
        {
            return new Slot(true, MergeObject(baseObject, localObject, serverObject, path, conflicts, options));
        }

        if (options.Sets.Contains(path) && AsArray(@base) is { } baseItems && AsArray(local) is { } localItems && AsArray(server) is { } serverItems)
        {
            return new Slot(true, MergeSet(baseItems, localItems, serverItems));
        }

        if (options.Counters.Contains(path) && AsNumber(@base) is { } baseCount && AsNumber(local) is { } localCount && AsNumber(server) is { } serverCount)
        {
            return new Slot(true, JsonValue.Create(serverCount + (localCount - baseCount)));
        }

        conflicts.Add(path.Length == 0 ? "/" : path);
        return server;
    }

    private static JsonObject MergeObject(JsonObject @base, JsonObject local, JsonObject server, string path, List<string> conflicts, ThreeWayMergeOptions options)
    {
        var result = new JsonObject();
        var keys = server.Select(p => p.Key).Concat(local.Select(p => p.Key)).Concat(@base.Select(p => p.Key)).Distinct(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var slot = MergeSlot(Get(@base, key), Get(local, key), Get(server, key), $"{path}/{key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)}", conflicts, options);
            if (slot.Present)
            {
                result[key] = slot.Value?.DeepClone();
            }
        }

        return result;
    }

    // An absent or null member counts as an empty set or zero, so a set or counter added on both sides still merges.
    private static List<JsonNode?>? AsArray(Slot slot) => slot switch
    {
        { Present: false } or { Value: null } => [],
        { Value: JsonArray array } => [.. array],
        _ => null,
    };

    private static decimal? AsNumber(Slot slot) => slot switch
    {
        { Present: false } or { Value: null } => 0m,
        { Value: JsonValue value } when value.TryGetValue<decimal>(out var number) => number,
        { Value: JsonValue value } when value.GetValueKind() == JsonValueKind.Number => decimal.Parse(value.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture),
        _ => null,
    };

    // Server order, then local additions in local order; an item removed on either side is removed.
    private static JsonArray MergeSet(List<JsonNode?> @base, List<JsonNode?> local, List<JsonNode?> server)
    {
        static bool Contains(List<JsonNode?> items, JsonNode? item) => items.Any(i => JsonNode.DeepEquals(i, item));
        var result = new JsonArray();
        foreach (var item in server.Where(i => Contains(local, i) || !Contains(@base, i)).Concat(local.Where(i => !Contains(@base, i) && !Contains(server, i))))
        {
            if (!result.Any(existing => JsonNode.DeepEquals(existing, item)))
            {
                result.Add(item?.DeepClone());
            }
        }

        return result;
    }

    private static Slot Get(JsonObject node, string key) =>
        node.TryGetPropertyValue(key, out var value) ? new Slot(true, value) : new Slot(false, null);
}

/// <summary>Semantic merge rules for members that are sets or counters, named by JSON Pointer (for example <c>/Tags</c>).</summary>
/// <remarks>
/// A set member is a JSON array whose items are compared by value: items added on either side are kept, items removed on
/// either side are removed, and duplicates collapse. A counter member is a number: the result is
/// <c>server + (local - base)</c>, so concurrent increments add up. Use the member names as they appear in JSON (after
/// any naming policy).
/// </remarks>
public sealed record ThreeWayMergeOptions
{
    /// <summary>No semantic members: every array and number is an atomic value.</summary>
    public static ThreeWayMergeOptions Default { get; } = new();

    /// <summary>JSON Pointers of members merged as sets.</summary>
    public IReadOnlySet<string> Sets { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>JSON Pointers of members merged as counters.</summary>
    public IReadOnlySet<string> Counters { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>The result of <see cref="ThreeWayMerge.Merge{TDocument}(TDocument, TDocument, TDocument, JsonTypeInfo{TDocument})"/>.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Merged">The merged document. Conflicting members hold the server value.</param>
/// <param name="Conflicts">JSON Pointers of members changed differently on both sides, and <see cref="ThreeWayMerge.DeletionConflict"/> for delete versus update.</param>
/// <param name="SameAsServer">The merge adds nothing to the server state (the local changes are already there).</param>
public sealed record ThreeWayMergeResult<TDocument>(TDocument Merged, IReadOnlyList<string> Conflicts, bool SameAsServer)
    where TDocument : class, ISyncEntity
{
    /// <summary>Whether the merge has no field conflicts.</summary>
    public bool IsClean => Conflicts.Count == 0;
}

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
