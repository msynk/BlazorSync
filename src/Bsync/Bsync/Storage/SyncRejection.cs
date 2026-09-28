namespace Bsync.Storage;

/// <summary>A permanent server rejection of one local revision.</summary>
/// <param name="Revision">The local revision that was rejected.</param>
/// <param name="ErrorCode">The server's machine-readable reason.</param>
/// <param name="Message">The server's explanation, if any.</param>
public sealed record SyncRejection(long Revision, string ErrorCode, string? Message);
