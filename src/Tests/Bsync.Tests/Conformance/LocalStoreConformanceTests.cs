using Bsync.Storage;
using Bsync.Testing;
using Xunit;

namespace Bsync.Tests.Conformance;

/// <summary>
/// Runs the shared <see cref="LocalStoreConformance"/> cases (also run in browsers for IndexedDB) against a
/// provider. A provider is not supported until every case passes.
/// </summary>
public abstract class LocalStoreConformanceTests
{
    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var conformanceCase in LocalStoreConformance.Cases)
        {
            data.Add(conformanceCase.Name);
        }

        return data;
    }

    /// <summary>Creates an empty store.</summary>
    protected abstract Task<ILocalStore<ConformanceDocument>> CreateStoreAsync();

    [Theory]
    [MemberData(nameof(CaseNames))]
    public Task Conformance(string name) =>
        LocalStoreConformance.Cases.Single(c => c.Name == name).RunAsync(CreateStoreAsync);
}
