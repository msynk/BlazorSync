namespace Bsync.Client;

/// <summary>What a host can do.</summary>
/// <param name="Host">A short description: <c>browser</c>, <c>native</c> or <c>server</c>.</param>
/// <param name="DurableOfflineWrites">Writes are kept on the device and uploaded later if the server is unreachable.</param>
/// <param name="WritesConfirmedByServer">A successful write has already been accepted by the server.</param>
/// <param name="LiveUpdates">Changes made elsewhere are announced without polling (announcements may still be missed).</param>
public sealed record SyncCapabilities(string Host, bool DurableOfflineWrites, bool WritesConfirmedByServer, bool LiveUpdates);
