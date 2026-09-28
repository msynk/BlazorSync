using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bsync.Protocol;

namespace Bsync.Transport.Http;

/// <summary>
/// An <see cref="ISyncTransport{TDocument}"/> for the HTTP binding (docs/protocol/v1.md §8). Failures are
/// classified: <see cref="SyncResetRequiredException"/> for <c>reset-required</c>,
/// <see cref="SyncProtocolException"/> for malformed responses, and <see cref="SyncTransportException"/> with
/// <see cref="SyncTransportException.IsTransient"/> and <see cref="SyncTransportException.RetryAfter"/> for
/// everything else, including network errors and timeouts. Caller cancellation surfaces as
/// <see cref="OperationCanceledException"/>.
/// </summary>
/// <remarks>
/// Credentials are the <see cref="HttpClient"/>'s concern (a <see cref="DelegatingHandler"/> adding a bearer
/// token, or browser cookies). This type never logs request or response bodies.
/// </remarks>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class HttpSyncTransport<TDocument> : ISyncTransport<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <summary>The protocol version this client speaks.</summary>
    public const string ProtocolVersion = "1";

    private readonly HttpClient _http;
    private readonly HttpSyncTransportOptions _options;
    private readonly SyncJsonTypes<TDocument> _json;
    private readonly string _pullPath;
    private readonly string _pushPath;
    private readonly string _hintsPath;

    /// <summary>Creates the transport.</summary>
    public HttpSyncTransport(HttpClient http, HttpSyncTransportOptions options, SyncJsonTypes<TDocument> json)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(json);
        if (!SyncIds.IsValid(options.Collection) || string.IsNullOrWhiteSpace(options.SchemaId))
        {
            throw new ArgumentException("A valid collection and schema id are required.", nameof(options));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.RequestTimeout, TimeSpan.Zero, nameof(options.RequestTimeout));
        _http = http;
        _options = options;
        _json = json;
        var basePath = $"{options.BasePath.Trim('/')}/collections/{Uri.EscapeDataString(options.Collection)}";
        _pullPath = $"{basePath}/pull";
        _pushPath = $"{basePath}/push";
        _hintsPath = $"{basePath}/hints";
    }

    /// <inheritdoc />
    public Task<PullResult<TDocument>> PullAsync(PullRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(_pullPath, request, _json.PullRequest, _json.PullResult, cancellationToken);

    /// <inheritdoc />
    public Task<PushResult<TDocument>> PushAsync(PushRequest<TDocument> request, CancellationToken cancellationToken = default) =>
        SendAsync(_pushPath, request, _json.PushRequest, _json.PushResult, cancellationToken);

    /// <summary>
    /// Opens the server's hint stream (Server-Sent Events) and yields a <see cref="StreamEventKind.Resync"/> event
    /// when the server announces changes (once on connect, then after each commit in the caller's scope). Hints
    /// carry no data: the caller pulls. The sequence ends when the server closes the stream; network failures
    /// surface as <see cref="SyncTransportException"/>. Throws <see cref="NotSupportedException"/> if the server
    /// has no hint endpoint.
    /// </summary>
    public async IAsyncEnumerable<StreamEvent<TDocument>> StreamAsync(
        Checkpoint since,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, _hintsPath);
        message.Headers.Add("Bsync-Protocol", ProtocolVersion);
        message.Headers.Add("Bsync-Schema", _options.SchemaId);
        message.Headers.Accept.ParseAdd("text/event-stream");
        message.Options.Set(new HttpRequestOptionsKey<bool>("WebAssemblyEnableStreamingResponse"), true);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException error)
        {
            throw new SyncTransportException(SyncErrorCodes.Unavailable, "The server could not be reached.", isTransient: true, innerException: error);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            {
                throw new NotSupportedException("The server does not offer change hints.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await ToExceptionAsync(response, cancellationToken).ConfigureAwait(false);
            }

            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
            var sawEvent = false;
            while (true)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (IOException error)
                {
                    throw new SyncTransportException(SyncErrorCodes.Unavailable, "The hint stream was interrupted.", isTransient: true, innerException: error);
                }

                if (line is null)
                {
                    yield break;
                }

                if (line.Length == 0)
                {
                    // A blank line ends an event; comments (": keep-alive") never set sawEvent.
                    if (sawEvent)
                    {
                        sawEvent = false;
                        yield return StreamEvent<TDocument>.Resync();
                    }
                }
                else if (!line.StartsWith(':'))
                {
                    sawEvent = true;
                }
            }
        }
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        JsonTypeInfo<TRequest> requestType,
        JsonTypeInfo<TResponse> responseType,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);

        using var message = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, requestType),
        };
        message.Headers.Add("Bsync-Protocol", ProtocolVersion);
        message.Headers.Add("Bsync-Schema", _options.SchemaId);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SyncTransportException(SyncErrorCodes.Unavailable, "The request timed out.", isTransient: true);
        }
        catch (HttpRequestException error)
        {
            throw new SyncTransportException(SyncErrorCodes.Unavailable, "The server could not be reached.", isTransient: true, innerException: error);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await ToExceptionAsync(response, timeout.Token).ConfigureAwait(false);
            }

            try
            {
                return await response.Content.ReadFromJsonAsync(responseType, timeout.Token).ConfigureAwait(false)
                    ?? throw new SyncProtocolException("The server returned an empty body.");
            }
            catch (JsonException error)
            {
                throw new SyncProtocolException("The server returned a malformed response.", error);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new SyncTransportException(SyncErrorCodes.Unavailable, "The response timed out.", isTransient: true);
            }
        }
    }

    private static async Task<Exception> ToExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var (code, detail, reason) = await ReadProblemAsync(response, cancellationToken).ConfigureAwait(false);
        var retryAfter = response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - DateTimeOffset.UtcNow is var wait && wait > TimeSpan.Zero ? wait : TimeSpan.Zero,
            _ => (TimeSpan?)null,
        };

        code ??= response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => SyncErrorCodes.Unauthorized,
            HttpStatusCode.Forbidden => SyncErrorCodes.Forbidden,
            HttpStatusCode.RequestEntityTooLarge => SyncErrorCodes.PayloadTooLarge,
            HttpStatusCode.TooManyRequests => SyncErrorCodes.RateLimited,
            _ when status >= 500 => SyncErrorCodes.Unavailable,
            _ => SyncErrorCodes.InvalidRequest,
        };

        var message = detail ?? $"The server answered {status} ({code}).";
        if (code == SyncErrorCodes.ResetRequired)
        {
            return new SyncResetRequiredException(message, reason ?? ResetReasons.Epoch);
        }

        var transient = code is SyncErrorCodes.RateLimited or SyncErrorCodes.Unavailable || status >= 500;
        return new SyncTransportException(code, message, transient, retryAfter, status);
    }

    private static async Task<(string? Code, string? Detail, string? Reason)> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (bytes.Length == 0 || bytes.Length > 64 * 1024)
            {
                return (null, null, null);
            }

            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null, null);
            }

            static string? Text(JsonElement root, string name) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            return (Text(root, "code"), Text(root, "detail"), Text(root, "reason"));
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }
}
