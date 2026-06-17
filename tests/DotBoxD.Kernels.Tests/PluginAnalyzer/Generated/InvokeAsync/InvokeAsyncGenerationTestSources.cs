using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

internal static class InvokeAsyncGenerationTestSources
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    internal const string NoCaptureSource = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;
        using DotBoxD.Kernels.Game.Plugin.Client;
        using DotBoxD.Kernels.Game.Server.Abstractions;

        namespace DotBoxD.Kernels.Game.Server.Abstractions
        {
            [DotBoxDService]
            public interface IGameWorldAccess
            {
                [HostBinding("host.world.getHealth", "game.world.monster.read.health", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int GetHealth(string entityId);
            }
        }

        namespace DotBoxD.Kernels.Game.Server.Abstractions.Ipc
        {
            public readonly record struct LiveSettingUpdate(string Name, string Value);

            public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
            {
                ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                ValueTask UpdateSettingsAsync(
                    string pluginId,
                    LiveSettingUpdate[] updates,
                    bool atomic = false,
                    CancellationToken ct = default);
                ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
            }
        }

        namespace DotBoxD.Kernels.Game.Plugin.Client
        {
            [GeneratePluginServer]
            public partial class RemotePluginServer : IGameWorldAccess;
        }

        namespace Sample
        {
            public static class Usage
            {
                public static ValueTask<int> Run(RemotePluginServer kernels)
                    => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                    {
                        var hp = world.GetHealth("monster-1");
                        return hp;
                    });
            }
        }
        """;

    internal const string CaptureBagSource = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;
        using DotBoxD.Kernels.Game.Plugin.Client;
        using DotBoxD.Kernels.Game.Server.Abstractions;

        namespace DotBoxD.Kernels.Game.Server.Abstractions
        {
            public sealed record MonsterSnapshot(string Id, string Name, int Health, int Level, int Position);

            [DotBoxDService]
            public interface IGameWorldAccess
            {
                [HostBinding("host.world.getMonster", "game.world.monster.read.snapshot", SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead)]
                MonsterSnapshot GetMonster(string entityId);
            }
        }

        namespace DotBoxD.Kernels.Game.Server.Abstractions.Ipc
        {
            public readonly record struct LiveSettingUpdate(string Name, string Value);

            public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
            {
                ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                ValueTask UpdateSettingsAsync(
                    string pluginId,
                    LiveSettingUpdate[] updates,
                    bool atomic = false,
                    CancellationToken ct = default);
                ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
            }
        }

        namespace DotBoxD.Kernels.Game.Plugin.Client
        {
            [GeneratePluginServer]
            public partial class RemotePluginServer : IGameWorldAccess;
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
                    => kernels.InvokeAsync(captures, async (IGameWorldAccess world, MonsterCapture bag) =>
                    {
                        var monster = world.GetMonster(bag.MonsterId);
                        bag.LastHealth = monster.Health;
                        return monster.Name;
                    });
            }
        }
        """;

    internal const string ObjectSurfaceSource = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;
        using DotBoxD.Kernels.Game.Plugin.Client;
        using DotBoxD.Kernels.Game.Server.Abstractions;

        namespace DotBoxD.Kernels.Game.Server.Abstractions
        {
            public sealed record MonsterSnapshot(string Id, string Name, int Health, int Level, int Position);

            [DotBoxDService]
            public interface IGameWorldAccess
            {
                [HostBinding("host.world.getMonster", "game.world.monster.read.snapshot", SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead)]
                MonsterSnapshot GetMonster(string entityId);
            }
        }

        namespace DotBoxD.Kernels.Game.Server.Abstractions.Ipc
        {
            public readonly record struct LiveSettingUpdate(string Name, string Value);

            public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
            {
                ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                ValueTask UpdateSettingsAsync(
                    string pluginId,
                    LiveSettingUpdate[] updates,
                    bool atomic = false,
                    CancellationToken ct = default);
                ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
            }
        }

        namespace DotBoxD.Kernels.Game.Plugin.Client
        {
            [GeneratePluginServer]
            public partial class RemotePluginServer : IGameWorldAccess;
        }

        namespace Sample
        {
            public static class Usage
            {
                public static ValueTask<int> Run(RemotePluginServer kernels)
                    => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                    {
                        var monster = world.GetMonster("monster-2");
                        return monster.Health;
                    });
            }
        }
        """;

    internal static string UsageSource(string usage)
        => """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;
        using DotBoxD.Kernels.Game.Plugin.Client;
        using DotBoxD.Kernels.Game.Server.Abstractions;

        namespace DotBoxD.Kernels.Game.Server.Abstractions
        {
            [DotBoxDService]
            public interface IGameWorldAccess
            {
                [HostBinding("host.world.getHealth", "game.world.monster.read.health", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int GetHealth(string entityId);
            }
        }

        namespace DotBoxD.Kernels.Game.Server.Abstractions.Ipc
        {
            public readonly record struct LiveSettingUpdate(string Name, string Value);

            public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
            {
                ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                ValueTask UpdateSettingsAsync(
                    string pluginId,
                    LiveSettingUpdate[] updates,
                    bool atomic = false,
                    CancellationToken ct = default);
                ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
            }
        }

        namespace DotBoxD.Kernels.Game.Plugin.Client
        {
            [GeneratePluginServer]
            public partial class RemotePluginServer : IGameWorldAccess;
        }

        namespace Sample
        {
            public static class Usage
            {
        """ + usage + """
            }
        }
        """;

    internal static GeneratorDriverRunResult RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDInvokeAsyncGeneratorTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Attributes.DotBoxDServiceAttribute).Assembly.Location)),
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
