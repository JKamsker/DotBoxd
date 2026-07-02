using DotBoxD.Kernels.Model;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Tests.Plugins.Json;

public sealed class PluginPackageJsonExportTests
{
    [Fact]
    public void Export_rejects_undefined_indexed_predicate_operator()
    {
        var package = FireDamagePluginPackage.Create();
        var subscription = package.Manifest.Subscriptions[0];
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions =
                [
                    subscription with
                    {
                        IndexedPredicates =
                            [new IndexedPredicate("Damage", (IndexPredicateOperator)999, 5, "int")]
                    }
                ]
            }
        };

        var ex = Assert.Throws<SandboxValidationException>(() => PluginPackageJsonSerializer.Export(invalid));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK046");
    }
}
