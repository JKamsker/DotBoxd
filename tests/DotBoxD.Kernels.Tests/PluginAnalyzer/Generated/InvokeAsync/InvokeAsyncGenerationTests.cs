using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncGenerationTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Block_body_no_capture_lambda_generates_anonymous_package()
    {
        var result = RunGenerator(NoCaptureSource);
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Contains("InvokeAsync_", source, StringComparison.Ordinal);
        Assert.Contains("$anon:", source, StringComparison.Ordinal);
        Assert.Contains("host.world.getHealth", source, StringComparison.Ordinal);
        Assert.Contains("game.world.monster.read.health", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Object_snapshot_member_access_generates_record_get_package()
    {
        var result = RunGenerator(ObjectSurfaceSource);
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Contains("InvokeAsync_", source, StringComparison.Ordinal);
        Assert.Contains("host.world.getMonster", source, StringComparison.Ordinal);
        Assert.Contains("game.world.monster.read.snapshot", source, StringComparison.Ordinal);
        Assert.Contains("record.get", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Expression_body_lambda_is_ignored()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync((IGameWorldAccess world) => world.GetHealth("monster-1"));
            """));

        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("InvokeAsync_", StringComparison.Ordinal));
    }

    [Fact]
    public void Implicit_capture_generates_reflection_arguments_and_sync_out()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
            {
                var monsterId = "monster-1";
                var lastHealth = 0;
                return kernels.InvokeAsync((IGameWorldAccess world) =>
                {
                    lastHealth = world.GetHealth(monsterId);
                    return lastHealth;
                });
            }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Contains("InvokeAsync_", source, StringComparison.Ordinal);
        Assert.Contains("__ReadCapture<", source, StringComparison.Ordinal);
        Assert.Contains("__WriteCapture(lambda, \"lastHealth\"", source, StringComparison.Ordinal);
        Assert.Contains("\\\"name\\\":\\\"monsterId\\\"", source, StringComparison.Ordinal);
        Assert.Contains("\\\"name\\\":\\\"lastHealth\\\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_capture_bag_generates_sync_in_and_sync_out_package()
    {
        var result = RunGenerator(CaptureBagSource);
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Contains("InvokeAsync_", source, StringComparison.Ordinal);
        Assert.Contains("\"parameters\\\":[{\\\"name\\\":\\\"bag\\\"", source, StringComparison.Ordinal);
        Assert.Contains("__syncOut_LastHealth", source, StringComparison.Ordinal);
        Assert.Contains("captures.LastHealth =", source, StringComparison.Ordinal);
    }

    private const string NoCaptureSource = """
        using System;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Kernels.Game.Plugin.Client;
        using DotBoxD.Kernels.Game.Server.Abstractions;

        namespace DotBoxD.Kernels.Game.Server.Abstractions
        {
            public interface IGameWorldAccess
            {
                [HostBinding("host.world.getHealth", "game.world.monster.read.health", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int GetHealth(string entityId);
            }
        }

        namespace DotBoxD.Kernels.Game.Plugin.Client
        {
            public sealed class RemotePluginServer
            {
                public ValueTask<T> InvokeAsync<T>(Func<IGameWorldAccess, T> lambda) => throw new InvalidOperationException();
            }
        }

        namespace Sample
        {
            public static class Usage
            {
                public static ValueTask<int> Run(RemotePluginServer kernels)
                    => kernels.InvokeAsync((IGameWorldAccess world) =>
                    {
                        var hp = world.GetHealth("monster-1");
                        return hp;
                    });
            }
        }
        """;

    private const string CaptureBagSource = """
        using System;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Kernels.Game.Plugin.Client;
        using DotBoxD.Kernels.Game.Server.Abstractions;

        namespace DotBoxD.Kernels.Game.Server.Abstractions
        {
            public sealed record MonsterSnapshot(string Id, string Name, int Health, int Level, int Position);

            public interface IGameWorldAccess
            {
                [HostBinding("host.world.getMonster", "game.world.monster.read.snapshot", SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead)]
                MonsterSnapshot GetMonster(string entityId);
            }
        }

        namespace DotBoxD.Kernels.Game.Plugin.Client
        {
            public delegate TReturn RemoteServerInvocation<TCaptures, TReturn>(
                IGameWorldAccess world,
                TCaptures captures);

            public sealed class RemotePluginServer
            {
                public ValueTask<T> InvokeAsync<TCaptures, T>(
                    TCaptures captures,
                    RemoteServerInvocation<TCaptures, T> lambda)
                    where TCaptures : class
                    => throw new InvalidOperationException();
            }
        }

        namespace Sample
        {
            public sealed class MonsterCapture
            {
                public string MonsterId { get; set; } = "";
                public int LastHealth { get; set; }
            }

            public static class Usage
            {
                public static ValueTask<string> Run(RemotePluginServer kernels, MonsterCapture captures)
                    => kernels.InvokeAsync(captures, (IGameWorldAccess world, MonsterCapture bag) =>
                    {
                        var monster = world.GetMonster(bag.MonsterId);
                        bag.LastHealth = monster.Health;
                        return monster.Name;
                    });
            }
        }
        """;

    private const string ObjectSurfaceSource = """
        using System;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Kernels.Game.Plugin.Client;
        using DotBoxD.Kernels.Game.Server.Abstractions;

        namespace DotBoxD.Kernels.Game.Server.Abstractions
        {
            public sealed record MonsterSnapshot(string Id, string Name, int Health, int Level, int Position);

            public interface IGameWorldAccess
            {
                [HostBinding("host.world.getMonster", "game.world.monster.read.snapshot", SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead)]
                MonsterSnapshot GetMonster(string entityId);
            }
        }

        namespace DotBoxD.Kernels.Game.Plugin.Client
        {
            public sealed class RemotePluginServer
            {
                public ValueTask<T> InvokeAsync<T>(Func<IGameWorldAccess, T> lambda) => throw new InvalidOperationException();
            }
        }

        namespace Sample
        {
            public static class Usage
            {
                public static ValueTask<int> Run(RemotePluginServer kernels)
                    => kernels.InvokeAsync((IGameWorldAccess world) =>
                    {
                        var monster = world.GetMonster("monster-2");
                        return monster.Health;
                    });
            }
        }
        """;

    private static string UsageSource(string usage)
        => """
        using System;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Kernels.Game.Plugin.Client;
        using DotBoxD.Kernels.Game.Server.Abstractions;

        namespace DotBoxD.Kernels.Game.Server.Abstractions
        {
            public interface IGameWorldAccess
            {
                [HostBinding("host.world.getHealth", "game.world.monster.read.health", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int GetHealth(string entityId);
            }
        }

        namespace DotBoxD.Kernels.Game.Plugin.Client
        {
            public sealed class RemotePluginServer
            {
                public ValueTask<T> InvokeAsync<T>(Func<IGameWorldAccess, T> lambda) => throw new InvalidOperationException();
            }
        }

        namespace Sample
        {
            public static class Usage
            {
        """ + usage + """
            }
        }
        """;

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDInvokeAsyncGeneratorTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
