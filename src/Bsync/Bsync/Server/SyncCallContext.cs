using System.Security.Claims;

namespace Bsync.Server;

/// <summary>
/// Who is calling an authority and in which scope. Built by the host from authenticated state (never from
/// request bodies), and passed identically by HTTP endpoints and in-process callers so both enforce the
/// same rules (I18).
/// </summary>
/// <param name="Principal">The authenticated caller. Unauthenticated callers have an identity with <c>IsAuthenticated == false</c>.</param>
/// <param name="Scope">
/// The data partition the caller may see (for example a tenant id), derived by the host from
/// <paramref name="Principal"/>. Feeds, receipts and checkpoints never cross scopes.
/// </param>
public sealed record SyncCallContext(ClaimsPrincipal Principal, string Scope)
{
    /// <summary>An unauthenticated caller in the <c>default</c> scope (tests and single-user hosts).</summary>
    public static SyncCallContext Anonymous { get; } = new(new ClaimsPrincipal(new ClaimsIdentity()), "default");
}
