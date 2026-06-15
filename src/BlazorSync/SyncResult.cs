namespace BlazorSync;

/// <summary>Summary statistics returned by a sync operation.</summary>
/// <param name="Pulled">Number of documents applied from the server.</param>
/// <param name="Pushed">Number of local writes accepted by the server.</param>
/// <param name="Conflicts">Number of conflicts encountered and resolved.</param>
public readonly record struct SyncResult(int Pulled, int Pushed, int Conflicts)
{
    /// <summary>Adds two results together (used to aggregate pull and push phases).</summary>
    public static SyncResult operator +(SyncResult a, SyncResult b) =>
        new(a.Pulled + b.Pulled, a.Pushed + b.Pushed, a.Conflicts + b.Conflicts);
}
