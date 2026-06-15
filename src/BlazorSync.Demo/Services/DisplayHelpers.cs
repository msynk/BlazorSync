using BlazorSync.Clocks;

namespace BlazorSync.Demo.Services;

/// <summary>Small formatting/colour helpers shared by the demo components.</summary>
public static class DisplayHelpers
{
    private static readonly string[] Palette =
    {
        "#4f8cff", "#ff7a59", "#27c498", "#b06bff", "#ffb020", "#ff5d8f", "#2bb1d6", "#8bc34a",
    };

    /// <summary>Returns a stable colour for a node/device id, for consistent visual identity.</summary>
    public static string ColorFor(string key)
    {
        var hash = 0;
        foreach (var c in key)
        {
            hash = (hash * 31 + c) & 0x7fffffff;
        }

        return Palette[hash % Palette.Length];
    }

    /// <summary>Formats an HLC timestamp as a compact local time plus its logical counter.</summary>
    public static string FormatHlc(HlcTimestamp ts)
    {
        if (ts.WallTime == 0)
        {
            return "—";
        }

        var local = DateTimeOffset.FromUnixTimeMilliseconds(ts.WallTime).LocalDateTime;
        return $"{local:HH:mm:ss.fff} ·{ts.Counter}";
    }

    /// <summary>Shortens a node id for compact display.</summary>
    public static string ShortNode(string node) =>
        node.Length <= 12 ? node : node[..12] + "…";
}
