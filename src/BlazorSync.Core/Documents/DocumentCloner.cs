using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace BlazorSync.Core.Documents;

/// <summary>
/// Produces independent copies of documents so that the engine can keep the "current" and "base"
/// states isolated (mutating one must never alias the other). The default implementation uses a
/// System.Text.Json round-trip, which works for any serializable POCO entity. Platforms that publish
/// trimmed/AOT (Blazor WASM) can supply a source-generated cloner via the engine options instead.
/// </summary>
public static class DocumentCloner
{
    /// <summary>
    /// Returns a deep copy of <paramref name="document"/> via JSON round-trip. Returns
    /// <see langword="null"/> when <paramref name="document"/> is <see langword="null"/>.
    /// </summary>
    [RequiresUnreferencedCode("Uses reflection-based JSON serialization to clone documents. Supply a custom cloner for trimmed or AOT targets.")]
    [RequiresDynamicCode("Uses reflection-based JSON serialization to clone documents. Supply a custom cloner for trimmed or AOT targets.")]
    [return: NotNullIfNotNull(nameof(document))]
    public static T? JsonClone<T>(T? document)
        where T : class
    {
        if (document is null)
        {
            return null;
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(document);
        return JsonSerializer.Deserialize<T>(json)
            ?? throw new InvalidOperationException($"Failed to clone document of type {typeof(T).Name}.");
    }
}
