using BlazorSync.Core;
using BlazorSync.Core.Clocks;

namespace BlazorSync.Core.Tests.TestSupport;

/// <summary>A simple sync entity used across the test suite.</summary>
public sealed class Note : ISyncEntity
{
    public string Id { get; set; } = Guid.CreateVersion7().ToString();

    public HlcTimestamp UpdatedAt { get; set; }

    public bool Deleted { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;
}
