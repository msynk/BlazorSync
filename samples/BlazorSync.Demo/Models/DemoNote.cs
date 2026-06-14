using BlazorSync.Core;
using BlazorSync.Core.Clocks;

namespace BlazorSync.Demo.Models;

/// <summary>
/// The synchronized entity used throughout the demo: a small note with a title, body and category.
/// </summary>
/// <remarks>
/// It implements <see cref="ISyncEntity"/> and provides an explicit <see cref="Clone"/>. Supplying a
/// hand-written clone (rather than relying on BlazorSync's default reflection-based JSON clone) is
/// the recommended pattern for Blazor WebAssembly, where trimming and AOT compilation make
/// reflection-based serialization undesirable.
/// </remarks>
public sealed class DemoNote : ISyncEntity
{
    /// <inheritdoc />
    public string Id { get; set; } = Guid.CreateVersion7().ToString();

    /// <inheritdoc />
    public HlcTimestamp UpdatedAt { get; set; }

    /// <inheritdoc />
    public bool Deleted { get; set; }

    /// <summary>The note title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The note body.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>A free-form category used to demonstrate field-level merges.</summary>
    public string Category { get; set; } = "General";

    /// <summary>Returns a deep, independent copy.</summary>
    public DemoNote Clone() => new()
    {
        Id = Id,
        UpdatedAt = UpdatedAt,
        Deleted = Deleted,
        Title = Title,
        Body = Body,
        Category = Category,
    };
}
