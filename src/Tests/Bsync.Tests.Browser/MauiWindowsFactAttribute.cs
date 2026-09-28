using Xunit;

namespace Bsync.Tests.Browser;

/// <summary>Skipped where the MAUI sample cannot run (not Windows, or not built because the maui-windows workload is missing).</summary>
public sealed class MauiWindowsFactAttribute : FactAttribute
{
    public MauiWindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "The MAUI sample is built for Windows here.";
        }
        else if (MauiHybridTests.Executable is null)
        {
            Skip = "The MAUI sample was not built (needs the maui-windows workload).";
        }
    }
}
