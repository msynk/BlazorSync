namespace Bsync.Demo.Services;

/// <summary>A single timestamped entry in the activity log.</summary>
/// <param name="Time">When it happened (wall clock).</param>
/// <param name="Kind">The category.</param>
/// <param name="Source">The device or component that produced it.</param>
/// <param name="Message">Human-readable description.</param>
public sealed record ActivityEntry(DateTimeOffset Time, ActivityKind Kind, string Source, string Message);
