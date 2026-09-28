using System.Reflection;
using Xunit;

namespace Bsync.Tests;

/// <summary>ADR-012: Bsync.Client carries no UI framework, so native hosts without Blazor can use it.</summary>
public sealed class PackageDependencyTests
{
    [Fact(DisplayName = "Bsync.Client references neither ASP.NET Core, Blazor nor JS interop")]
    public void ClientIsUiIndependent()
    {
        var references = Assembly.Load("Bsync.Client").GetReferencedAssemblies().Select(r => r.Name!).ToList();

        Assert.Contains("Bsync", references);
        Assert.DoesNotContain(references, r => r.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) || r.StartsWith("Microsoft.JSInterop", StringComparison.Ordinal));
    }
}
