using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientExtensionValidationTests
{
    private const string ServiceBackedReceiverExtensionSource = """
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IRemoteMonsterControl;

        public interface IMonsterKillerService
        {
            ValueTask<int> KillMonstersAsync(int monsterId);
        }

        [ServerExtensionClient(typeof(IRemoteMonsterControl))]
        [ServerExtension("monster-killer", typeof(IMonsterKillerService))]
        public sealed partial class MonsterKillerKernel
        {
            public int KillMonsters(int monsterId, HookContext ctx) => monsterId;
        }
        """;

    [Fact]
    public void Service_backed_receiver_extensions_require_csharp_14()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(
            ServiceBackedReceiverExtensionSource,
            LanguageVersion.CSharp13);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("C# 14", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("CS", StringComparison.Ordinal));
    }

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

            [RpcService]
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
