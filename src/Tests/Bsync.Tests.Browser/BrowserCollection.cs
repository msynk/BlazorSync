using Bsync.Protocol;
using Bsync.Server;
using Bsync.Server.AspNetCore;
using Bsync.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Xunit;

namespace Bsync.Tests.Browser;

[CollectionDefinition(Name)]
public sealed class BrowserCollection : ICollectionFixture<BrowserFixture>
{
    public const string Name = "browsers";
}
