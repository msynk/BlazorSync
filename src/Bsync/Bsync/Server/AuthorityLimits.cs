using Bsync.Protocol;

namespace Bsync.Server;

/// <summary>Limits an authority enforces and advertises.</summary>
/// <param name="MaxOperationsPerPush">Maximum operations in one push request.</param>
/// <param name="MaxPageSize">Maximum changes returned by one pull.</param>
public sealed record AuthorityLimits(int MaxOperationsPerPush, int MaxPageSize);
