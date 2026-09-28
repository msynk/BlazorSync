using System.Text.Json.Serialization;
using Bsync.Clocks;
using Bsync.Documents;
using Bsync.Protocol;
using Bsync.Storage;
using Bsync.Storage.IndexedDb;
using Bsync.Testing;
using Bsync.Transport.Http;
using Microsoft.JSInterop;

namespace Bsync.Tests.BrowserHost;

public sealed record DocumentView(string Id, string Title, bool Deleted, bool Dirty);
