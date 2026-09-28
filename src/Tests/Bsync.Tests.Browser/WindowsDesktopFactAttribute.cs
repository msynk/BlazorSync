using Xunit;

namespace Bsync.Tests.Browser;

/// <summary>Skipped where the WPF Hybrid sample cannot run (not Windows, or not built).</summary>
public sealed class WindowsDesktopFactAttribute : FactAttribute
{
    public WindowsDesktopFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "The WPF Blazor Hybrid sample runs on Windows only.";
        }
        else if (!File.Exists(WpfHybridTests.Executable))
        {
            Skip = $"The WPF sample was not built ({WpfHybridTests.Executable}).";
        }
    }
}
