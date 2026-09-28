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
/// array conflict even if they touched different elements; sets and counters are not merged semantically.</item>
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
        where TDocument : class, ISyncEntity
    {
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
            merged = MergeSlot(new Slot(true, baseNode), new Slot(true, localNode), new Slot(true, serverNode), string.Empty, conflicts).Value;
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

    private static Slot MergeSlot(Slot @base, Slot local, Slot server, string path, List<string> conflicts)
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
            return new Slot(true, MergeObject(baseObject, localObject, serverObject, path, conflicts));
        }

        conflicts.Add(path.Length == 0 ? "/" : path);
        return server;
    }

    private static JsonObject MergeObject(JsonObject @base, JsonObject local, JsonObject server, string path, List<string> conflicts)
    {
        var result = new JsonObject();
        var keys = server.Select(p => p.Key).Concat(local.Select(p => p.Key)).Concat(@base.Select(p => p.Key)).Distinct(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var slot = MergeSlot(Get(@base, key), Get(local, key), Get(server, key), $"{path}/{key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)}", conflicts);
            if (slot.Present)
            {
                result[key] = slot.Value?.DeepClone();
            }
        }

        return result;
    }

    private static Slot Get(JsonObject node, string key) =>
        node.TryGetPropertyValue(key, out var value) ? new Slot(true, value) : new Slot(false, null);
}

/// <summary>The result of <see cref="ThreeWayMerge.Merge{TDocument}"/>.</summary>
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
public sealed class ThreeWayMergeConflictHandler<TDocument>(JsonTypeInfo<TDocument> typeInfo, IConflictHandler<TDocument>? fallback = null) : IConflictHandler<TDocument>
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

        var result = ThreeWayMerge.Merge(context.AssumedMaster, context.Fork, context.RealMaster, _typeInfo);
        if (!result.IsClean)
        {
            return _fallback.Resolve(context);
        }

        return result.SameAsServer ? ConflictResolution<TDocument>.AcceptMaster() : ConflictResolution<TDocument>.Resolve(result.Merged);
    }
}
