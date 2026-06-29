using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientExtensionValidationTests
{
    [Theory]
    [InlineData("[ServerExtensionClient(typeof(IRemoteMonsterControl), \" \")]", "property")]
    [InlineData("[ServerExtensionMethod(typeof(IRemoteMonsterControl), \" \")]", "method")]
    public void Blank_generated_extension_name_reports_diagnostic(string attribute, string memberKind)
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics($$"""
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Services.Attributes;

            namespace Sample;

            [DotBoxDService]
            public interface IRemoteMonsterControl;

            public interface IMonsterKillerService
            {
                ValueTask<int> KillMonstersAsync(int monsterId);
            }

            {{(memberKind == "property" ? attribute : "")}}
            [ServerExtension("monster-killer", typeof(IMonsterKillerService))]
            public sealed partial class MonsterKillerKernel
            {
                {{(memberKind == "method" ? attribute : "")}}
                public int KillMonsters(int monsterId, HookContext ctx) => monsterId;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains(memberKind, StringComparison.Ordinal) &&
                 d.GetMessage().Contains("must not be empty or whitespace", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("CS", StringComparison.Ordinal));
    }
}
