using BlazorSync.Demo.Models;

namespace BlazorSync.Demo.Services;

/// <summary>A note paired with whether it has a local, not-yet-pushed change.</summary>
/// <param name="Note">The note.</param>
/// <param name="IsDirty">Whether the note is in the push queue.</param>
public sealed record NoteView(DemoNote Note, bool IsDirty);
