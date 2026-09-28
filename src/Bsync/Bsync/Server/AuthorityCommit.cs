using Bsync.Protocol;

namespace Bsync.Server;

/// <summary>Published by an authority after a push committed changes. A hint only: it may be missed.</summary>
/// <param name="Scope">The scope whose documents changed.</param>
/// <param name="Ids">The changed document ids.</param>
public sealed record AuthorityCommit(string Scope, IReadOnlyList<string> Ids);
