namespace Bsync.Client;

/// <summary>A bounded, in-memory query over a collection.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed record SyncQuery<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <summary>The largest allowed <see cref="Limit"/>.</summary>
    public const int MaxLimit = 1000;

    /// <summary>Keeps only matching documents. Default: all.</summary>
    public Func<TDocument, bool>? Where { get; init; }

    /// <summary>Sort order. Default: by id (ordinal).</summary>
    public Comparison<TDocument>? Order { get; init; }

    /// <summary>Maximum number of documents returned (1 to <see cref="MaxLimit"/>). Default 100.</summary>
    public int Limit { get; init; } = 100;

    /// <summary>
    /// Evaluates the query in memory: drops deleted documents, keeps those matching <see cref="Where"/>, sorts by
    /// <see cref="Order"/> and returns at most <see cref="Limit"/>. For <see cref="ISyncCollection{TDocument}"/>
    /// implementations that read their documents from elsewhere.
    /// </summary>
    /// <param name="documents">The documents to evaluate.</param>
    public IReadOnlyList<TDocument> Apply(IEnumerable<TDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        Queries.Validate(this);

        var matching = documents.Where(d => !d.Deleted && (Where?.Invoke(d) ?? true)).ToList();
        matching.Sort(Order ?? ((a, b) => string.CompareOrdinal(a.Id, b.Id)));
        return matching.Count > Limit ? matching.GetRange(0, Limit) : matching;
    }
}
