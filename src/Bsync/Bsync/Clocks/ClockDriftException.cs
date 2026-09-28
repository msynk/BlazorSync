namespace Bsync.Clocks;

/// <summary>Thrown when a remote timestamp is further ahead of local physical time than allowed.</summary>
public sealed class ClockDriftException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public ClockDriftException(HlcTimestamp remote, long physicalNow, TimeSpan maxForwardDrift)
        : base($"Remote timestamp {remote} is more than {maxForwardDrift} ahead of local physical time {physicalNow}.")
    {
        Remote = remote;
    }

    /// <summary>The rejected remote timestamp.</summary>
    public HlcTimestamp Remote { get; }
}
