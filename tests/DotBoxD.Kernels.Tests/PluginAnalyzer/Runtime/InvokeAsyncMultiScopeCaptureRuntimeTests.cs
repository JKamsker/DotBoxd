using System.Reflection;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class InvokeAsyncMultiScopeCaptureRuntimeTests
{
    [Fact]
    public async Task Generated_interceptor_reads_and_writes_captures_across_nested_display_classes()
    {
        var assembly = Compile(Source);
        var wire = Activator.CreateInstance(assembly.GetType("Sample.RecordingControlService", throwOnError: true)!)!;
        var controlType = assembly.GetType("DotBoxD.Kernels.Game.Plugin.Client.RemotePluginServer", true)!;
        var control = Activator.CreateInstance(controlType, [wire, null])!;
        var run = assembly.GetType("Sample.Usage", true)!
            .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;

        var result = await AwaitValueTaskResult<int>(run.Invoke(null, [control])!);

        // sum (item 0 = 95) + lastHealth written back across scopes (item 1 = 80) = 175.
        Assert.Equal(175, result);

        var captured = (int[])wire.GetType().GetProperty("CapturedArguments")!.GetValue(wire)!;
        // outerBonus (outer scope) = 5, innerBonus (loop scope) = 10, lastHealth (outer scope) = 0.
        Assert.Equal([0, 5, 10], captured.OrderBy(static value => value).ToArray());
    }

    private const string Source = """
        using System;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;
        using DotBoxD.Kernels.Game.Plugin.Client;
        using DotBoxD.Kernels.Game.Server.Abstractions;
        using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;

        namespace DotBoxD.Kernels.Game.Server.Abstractions
        {
            [DotBoxDService]
            public interface IGameWorldAccess
            {
                [HostBinding("host.world.getHealth", "game.world.monster.read.health", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int GetHealth(string entityId);
            }
        }

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static IGameWorldAccess GetGameWorldAccess(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new InvalidOperationException("not used");
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
            [GeneratePluginServer(Context = typeof(RemotePluginContext))]
            public partial class RemotePluginServer : IGameWorldAccess;

            public sealed partial class RemotePluginContext;
        }

        namespace Sample
        {
            public static class Usage
            {
                public static async ValueTask<int> Run(RemotePluginServer kernels)
                {
                    var outerBonus = 5;
                    var lastHealth = 0;
                    var sum = 0;
                    for (var i = 0; i < 1; i++)
                    {
                        var innerBonus = 10;
                        sum = await kernels.InvokeAsync(async (IGameWorldAccess world) =>
                        {
                            var hp = world.GetHealth("monster-1");
                            lastHealth = hp;
                            return hp + outerBonus + innerBonus;
                        });
                    }

                    return sum + lastHealth;
                }
            }

            public sealed class RecordingControlService : IGamePluginControlService
            {
                public int[] CapturedArguments { get; private set; } = Array.Empty<int>();

                public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
                    => InstallPackageAsync(packageJson);

                public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default)
                    => InstallPackageAsync(packageJson);

                public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
                    => InstallPackageAsync(packageJson);

                public ValueTask UpdateSettingsAsync(
                    string pluginId,
                    LiveSettingUpdate[] updates,
                    bool atomic = false,
                    CancellationToken ct = default)
                    => default;

                public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default)
                    => default;

                public ValueTask<byte[]> InvokeServerExtensionAsync(
                    string pluginId,
                    byte[] arguments,
                    CancellationToken cancellationToken = default)
                {
                    var values = KernelRpcBinaryCodec.DecodeArguments(arguments);
                    CapturedArguments = values.Select(value => value.Int32Value).ToArray();
                    return ValueTask.FromResult(KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
                    [
                        KernelRpcValue.Int32(95),
                        KernelRpcValue.Int32(80)
                    ])));
                }

                private ValueTask<string> InstallPackageAsync(string packageJson)
                {
                    var package = DotBoxD.Plugins.Json.PluginPackageJsonSerializer.Import(packageJson);
                    return ValueTask.FromResult(package.Manifest.PluginId);
                }
            }
        }
        """;

    private static Assembly Compile(string source)
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithFeatures(
            [
                new KeyValuePair<string, string>(
                    "InterceptorsNamespaces",
                    DotBoxDGenerationNames.TypeNames.GeneratedInterceptorsNamespace)
            ]);
        var compilation = CSharpCompilation.Create(
            "DotBoxDInvokeAsyncMultiScopeCaptureRuntimeTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Attributes.DotBoxDServiceAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Peer.RpcPeerSession).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Pushdown.Services.RpcMessagePackIpc).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static async Task<T> AwaitValueTaskResult<T>(object valueTask)
    {
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        var task = (Task<T>)asTask.Invoke(valueTask, null)!;
        return await task.ConfigureAwait(false);
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
