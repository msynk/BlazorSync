using Npgsql;
using Xunit;

namespace Bsync.Tests.Browser;

/// <summary>Skipped unless <c>BSYNC_POSTGRES</c> names a PostgreSQL server where the user may create databases.</summary>
public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BSYNC_POSTGRES")))
        {
            Skip = "Set BSYNC_POSTGRES to run tests against PostgreSQL.";
        }
    }
}
