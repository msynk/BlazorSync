using System.Text.Json.Serialization;
using Bsync.Clocks;
using Bsync.Conflicts;
using Bsync.Tests.TestSupport;
using Xunit;

namespace Bsync.Tests;

public sealed class Address
{
    public string Street { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;
}
