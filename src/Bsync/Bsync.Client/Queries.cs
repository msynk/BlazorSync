namespace Bsync.Client;

/// <summary>Shared query evaluation.</summary>
internal static class Queries
{
    public const int PageSize = 200;

    public static void Validate<TDocument>(SyncQuery<TDocument> query)
        where TDocument : class, ISyncEntity
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Limit, 1, nameof(query.Limit));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Limit, SyncQuery<TDocument>.MaxLimit, nameof(query.Limit));
    }
}
