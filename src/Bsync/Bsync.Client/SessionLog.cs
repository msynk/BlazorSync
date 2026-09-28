using Bsync.Conflicts;
using Bsync.Storage;
using Bsync.Transport;
using Microsoft.Extensions.Logging;

namespace Bsync.Client;

/// <summary>Structured, allocation-free log messages for <see cref="SyncSession{TDocument}"/>.</summary>
internal static partial class SessionLog
{
    [LoggerMessage(EventId = 1, EventName = "SyncStateChanged", Message = "Sync ({Host}) {Previous} -> {State}; {Pending} pending. {Detail}")]
    public static partial void StateChanged(ILogger logger, LogLevel level, string host, SyncState previous, SyncState state, int pending, string? detail);

    [LoggerMessage(EventId = 2, EventName = "SyncProtocolError", Level = LogLevel.Error, Message = "Sync ({Host}) stopped: the server sent an invalid response.")]
    public static partial void ProtocolError(ILogger logger, string host, Exception error);

    [LoggerMessage(EventId = 3, EventName = "SyncUnexpectedError", Level = LogLevel.Error, Message = "Sync ({Host}) stopped by an unexpected error.")]
    public static partial void UnexpectedError(ILogger logger, string host, Exception error);
}
