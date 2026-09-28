using System.Security.Claims;
using Bsync.Clocks;
using Bsync.Protocol;
using Bsync.Server;
using Bsync.Server.PostgreSql;
using Bsync.Tests.Conformance;
using Bsync.Tests.TestSupport;
using Bsync.Transport;
using Xunit;

namespace Bsync.Tests.PostgreSql;

/// <summary>The shared authority conformance suite against PostgreSQL (one collection per test, one database per class).</summary>
public sealed class PostgreSqlAuthorityConformanceTests(PostgresFixture fixture) : AuthorityConformanceTests, IClassFixture<PostgresFixture>
{
    protected override ISyncTransport<Note> CreateAuthority(IPhysicalClock clock, Func<PushOperation<Note>, Note?, string?>? validator = null)
    {
        var authority = fixture.Database.AuthorityAsync(
            $"c{Guid.NewGuid():N}",
            configure: o => new PostgreSqlSyncAuthorityOptions<Note>
            {
                DataSource = o.DataSource,
                DocumentType = o.DocumentType,
                Collection = o.Collection,
                PhysicalClock = clock,
                Validator = validator,
            }).GetAwaiter().GetResult();
        return new InProcessTransport<Note>(authority);
    }
}
