using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bsync.Clocks;
using Bsync.Documents;
using Bsync.Protocol;

namespace Bsync.Server;

/// <summary>A point-in-time copy of an <see cref="InMemorySyncServer{TDocument}"/>'s state.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class InMemorySyncServerBackup<TDocument>
    where TDocument : class, ISyncEntity
{
    internal InMemorySyncServerBackup(
        IReadOnlyDictionary<string, (TDocument Document, long Version)> documents,
        IReadOnlyDictionary<string, (string Fingerprint, PushOutcome<TDocument> Outcome)> receipts,
        long sequence,
        long purgedThrough)
    {
        Documents = documents;
        Receipts = receipts;
        Sequence = sequence;
        PurgedThrough = purgedThrough;
    }

    internal long PurgedThrough { get; }

    internal IReadOnlyDictionary<string, (TDocument Document, long Version)> Documents { get; }

    internal IReadOnlyDictionary<string, (string Fingerprint, PushOutcome<TDocument> Outcome)> Receipts { get; }

    /// <summary>The highest version issued when the backup was taken.</summary>
    public long Sequence { get; }
}
