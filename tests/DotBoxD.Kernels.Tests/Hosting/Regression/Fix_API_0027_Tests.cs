using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Hosting.Regression;

public sealed class Fix_API_0027_Tests
{
    private static readonly string[] CurrentServerExtensionDocumentation =
    [
        "README.md",
        Path.Combine("docs-site", "src", "content", "docs", "index.mdx"),
        Path.Combine("docs-site", "src", "content", "docs", "getting-started.md"),
        Path.Combine("docs-site", "src", "content", "docs", "concepts", "pushdown.md"),
        Path.Combine("docs-site", "src", "content", "docs", "tutorials", "pushdown-server-extension.md"),
        Path.Combine("docs", "Specs", "Addendum", "Examples.md"),
        Path.Combine("docs", "design", "plugin-fluent-hooks-api", "followups.md"),
        Path.Combine("docs", "design", "remote-plugin-server-builder", "invoke-async.md"),
    ];

    [Fact]
    public void Documented_pushdown_samples_use_current_server_extension_surface()
    {
        var readme = ReadRepositoryText("README.md");
        var concepts = ReadRepositoryText(Path.Combine(
            "docs-site",
            "src",
            "content",
            "docs",
            "concepts",
            "pushdown.md"));
        var addendum = ReadRepositoryText(Path.Combine("docs", "Specs", "Addendum", "Examples.md"));
        var docs = string.Join(Environment.NewLine, CurrentServerExtensionDocumentation.Select(ReadRepositoryText));

        Assert.Contains("[ServerExtension(\"monster-killer\", typeof(IMonsterKillerService))]", readme, StringComparison.Ordinal);
        Assert.Contains("RegisterServerExtensionAsync<IMonsterKillerService, MonsterKillerKernel>", readme, StringComparison.Ordinal);
        Assert.Contains("ServerExtension<IMonsterKillerService>().KillMonsters(ids)", readme, StringComparison.Ordinal);
        Assert.Contains("[ServerExtension", concepts, StringComparison.Ordinal);
        Assert.Contains("### 5. Use Server Extensions For Pushdown", addendum, StringComparison.Ordinal);

        foreach (var staleToken in StaleServerExtensionTokens())
        {
            Assert.DoesNotContain(staleToken, docs, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("kernel RPC", docs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Documented_pushdown_server_extension_shape_compiles_through_generator()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System.Collections.Generic;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;

            namespace Sample;

            public interface IGameWorld
            {
                [HostBinding("host.world.kill", "game.world.monster.write.kill",
                    SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
                bool Kill(int id);
            }

            public interface IMonsterKillerService
            {
                List<KillResult> KillMonsters(List<int> monsterIds);
            }

            public readonly record struct KillResult(int MonsterId, bool Success);

            [ServerExtension("monster-killer", typeof(IMonsterKillerService))]
            public sealed partial class MonsterKillerKernel
            {
                public List<KillResult> KillMonsters(List<int> monsterIds, HookContext ctx)
                {
                    var results = new List<KillResult>();
                    foreach (var id in monsterIds)
                    {
                        results.Add(new KillResult(id, ctx.Host<IGameWorld>().Kill(id)));
                    }

                    return results;
                }
            }
            """);

        Assert.NotNull(assembly.GetType("Sample.MonsterKillerPluginPackage"));
        Assert.NotNull(assembly.GetType("Sample.MonsterKillerKernelServerExtensionClient"));
    }

    private static string ReadRepositoryText(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath);
        Assert.True(File.Exists(path), $"Missing repository file: {path}");
        return File.ReadAllText(path);
    }

    private static string[] StaleServerExtensionTokens()
        =>
        [
            "KernelRpcService",
            "KernelRpcClientMethod",
            "RegisterKernelRpcService",
            "RegisterRpcServiceAsync",
            "RpcService<",
            "server.KernelRpc",
            "SetupKernelRpc",
            "RemoteKernelRpcControl",
            "KernelRpcRegistrationAccumulator",
        ];

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DotBoxD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
