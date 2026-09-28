using System.Text.Json.Serialization;
using Bsync;
using Bsync.Documents;
using Bsync.Protocol;
using Bsync.Storage.Sqlite;
using Bsync.Transport;

internal sealed class NoNetwork : ISyncTransport<CrashNote>
{
    public Task<PullResult<CrashNote>> PullAsync(PullRequest request, CancellationToken cancellationToken = default) => throw new IOException("offline");

    public Task<PushResult<CrashNote>> PushAsync(PushRequest<CrashNote> request, CancellationToken cancellationToken = default) => throw new IOException("offline");

    public IAsyncEnumerable<StreamEvent<CrashNote>> StreamAsync(Checkpoint since, CancellationToken cancellationToken = default) => throw new IOException("offline");
}
