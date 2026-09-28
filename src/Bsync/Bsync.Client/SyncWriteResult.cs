namespace Bsync.Client;

/// <summary>The result of a write.</summary>
/// <param name="Id">The document id.</param>
/// <param name="Confirmation">How far the write is confirmed.</param>
/// <param name="Message">An error code or explanation for <see cref="SyncConfirmation.Rejected"/> and <see cref="SyncConfirmation.Conflict"/>.</param>
public sealed record SyncWriteResult(string Id, SyncConfirmation Confirmation, string? Message = null)
{
    /// <summary>Whether the write took effect (locally or on the server).</summary>
    public bool Succeeded => Confirmation is SyncConfirmation.SavedLocally or SyncConfirmation.AcceptedByServer;
}
