using System.Diagnostics.CodeAnalysis;
using Bsync.Clocks;
using Bsync.Diagnostics;
using Bsync.Documents;
using Bsync.Protocol;
using Bsync.Storage;
using Bsync.Transport;

namespace Bsync;

/// <summary>The local commit receipt returned by <see cref="SyncEngine{TDocument}.WriteAsync"/> and <see cref="SyncEngine{TDocument}.DeleteAsync"/>.</summary>
/// <remarks>
/// A receipt means the write is committed to the local store (with the durability of that store) and
/// queued for upload. It says nothing about server acceptance.
/// </remarks>
/// <param name="Id">The document id.</param>
/// <param name="LocalRevision">The local revision created by the write.</param>
/// <param name="UpdatedAt">The origin timestamp stamped on the stored document.</param>
public readonly record struct LocalWriteReceipt(string Id, long LocalRevision, HlcTimestamp UpdatedAt);
