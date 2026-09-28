namespace Bsync;

/// <summary>Request-level error codes (docs/protocol/v1.md §7). Operation-level codes are in <c>PushErrorCodes</c>.</summary>
public static class SyncErrorCodes
{
    /// <summary>The checkpoint cannot be served; the replica must reset.</summary>
    public const string ResetRequired = "reset-required";

    /// <summary>The protocol or application schema version is not supported.</summary>
    public const string UpgradeRequired = "upgrade-required";

    /// <summary>The request exceeds the authority's limits.</summary>
    public const string PayloadTooLarge = "payload-too-large";

    /// <summary>Too many requests; retry after the indicated delay.</summary>
    public const string RateLimited = "rate-limited";

    /// <summary>Credentials are missing or expired.</summary>
    public const string Unauthorized = "unauthorized";

    /// <summary>The caller may not use this collection or scope.</summary>
    public const string Forbidden = "forbidden";

    /// <summary>The request is malformed.</summary>
    public const string InvalidRequest = "invalid-request";

    /// <summary>The authority is temporarily unavailable.</summary>
    public const string Unavailable = "unavailable";
}

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
