namespace Bsync;

/// <summary>
/// A request-level failure reported by the authority or the transport. <see cref="IsTransient"/> tells the
/// caller whether retrying the same request later can succeed. Any push that fails this way has an unknown
/// outcome; the engine keeps its operations pending and resends them with the same ids.
/// </summary>
public class SyncTransportException : Exception
{
    /// <summary>Creates the exception.</summary>
    public SyncTransportException(string errorCode, string message, bool isTransient, TimeSpan? retryAfter = null, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        IsTransient = isTransient;
        RetryAfter = retryAfter;
        StatusCode = statusCode;
    }

    /// <summary>A <see cref="SyncErrorCodes"/> value.</summary>
    public string ErrorCode { get; }

    /// <summary>Whether a later retry of the same request may succeed.</summary>
    public bool IsTransient { get; }

    /// <summary>The server's requested delay before retrying, if any.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>The HTTP status code, when the failure came from an HTTP response.</summary>
    public int? StatusCode { get; }
}
