using System.Reflection;
using Xunit;

namespace Bsync.Tests;

/// <summary>ADR-012: Bsync (core, client and HTTP transport) carries no UI framework, so native hosts without Blazor can use it.</summary>
public sealed class PackageDependencyTests
{
    [Fact(DisplayName = "Bsync references neither ASP.NET Core, Blazor nor JS interop")]
    public void CoreIsUiIndependent()
    {
        var references = Assembly.Load("Bsync").GetReferencedAssemblies().Select(r => r.Name!).ToList();

        Assert.DoesNotContain(references, r => r.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) || r.StartsWith("Microsoft.JSInterop", StringComparison.Ordinal));
    }
}
