using Bsync.Protocol;
using Bsync.Storage;
using Bsync.Transport;

namespace Bsync.Tests.TestSupport;

/// <summary>Thrown by fault-injecting doubles to simulate a crash or I/O failure.</summary>
public sealed class InjectedFaultException(string message) : IOException(message);
