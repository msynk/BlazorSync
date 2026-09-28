namespace Bsync.Storage;

/// <summary>Identifies a replica's storage and its current incarnation.</summary>
/// <param name="ReplicaId">Generated once when the storage is created.</param>
/// <param name="Incarnation">
/// Changes when the storage may have been copied or restored (for example a device backup). Use it, or a value
/// derived from it, as the HLC node id so a copy never reuses the original's timestamps.
/// </param>
public sealed record ReplicaIdentity(string ReplicaId, string Incarnation);
