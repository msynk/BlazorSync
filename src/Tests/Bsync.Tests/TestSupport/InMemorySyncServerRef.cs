using Bsync.Clocks;
using Bsync.Storage;
using Bsync.Transport;

namespace Bsync.Tests.TestSupport;

/// <summary>Holds a server so replicas can share it, and lets tests replace it (restore from backup).</summary>
public sealed class InMemorySyncServerRef(Server.InMemorySyncServer<Note> server)
{
    public Server.InMemorySyncServer<Note> Server { get; private set; } = server;

    /// <summary>
    /// Replaces the server with one restored from <paramref name="backup"/>, with a new epoch and a version
    /// floor above everything the replaced server issued.
    /// </summary>
    public void Restore(Server.InMemorySyncServerBackup<Note> backup, IPhysicalClock? clock = null)
    {
        var options = NoteJson.ServerOptions(clock);
        Server = new Server.InMemorySyncServer<Note>(new Server.InMemorySyncServerOptions<Note>
        {
            Cloner = options.Cloner,
            Fingerprint = options.Fingerprint,
            PhysicalClock = options.PhysicalClock,
            RestoreFrom = backup,
            VersionFloor = Server.HighestVersion,
        });
    }

    public static InMemorySyncServerRef Create(IPhysicalClock? clock = null) =>
        new(new Server.InMemorySyncServer<Note>(NoteJson.ServerOptions(clock)));

    public Note Get(string id) => Server.Snapshot().Single(n => n.Id == id);
}
