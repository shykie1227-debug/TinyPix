using TinyPix.Core.Cache;
using TinyPix.Core.History;
using TinyPix.Core.Models;
using TinyPix.Core.Preview;
using TinyPix.Core.Settings;

namespace TinyPix.Core.Tests;

public sealed class CoreBoundaryTests
{
    [Fact]
    public void Core_contracts_cover_all_external_capabilities()
    {
        Type[] contracts =
        [
            typeof(IMediaPreviewService),
            typeof(IHistoryRepository),
            typeof(ISettingsStore),
            typeof(ICacheService),
            typeof(IModelRuntime),
        ];

        Assert.All(contracts, contract => Assert.True(contract.IsInterface));
    }

    [Fact]
    public void Core_assembly_does_not_reference_other_TinyPix_modules()
    {
        string[] references = typeof(ToolCatalogTests).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();
        string[] coreReferences = typeof(TinyPix.Core.Tools.ToolCatalog).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(coreReferences, name =>
            name.StartsWith("TinyPix.", StringComparison.Ordinal) && name != "TinyPix.Core");
        Assert.Contains("TinyPix.Core", references);
    }
}
