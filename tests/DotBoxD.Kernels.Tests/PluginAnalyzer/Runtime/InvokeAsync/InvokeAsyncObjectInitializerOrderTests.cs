using System.Reflection;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

/// <summary>
/// Proof for review finding #20 on the InvokeAsync lowering path: object-initializer members must be evaluated
/// in SOURCE (lexical) order. The lambda below writes the members B-then-A while the DTO declares its fields
/// A-then-B; <c>HoistInitializerMember</c> hoists each member into an ordered prelude temp so the side-effectful
/// host calls run in the written order. The InvokeAsync factory constructs the lowerer with a null
/// <c>_expressionPrelude</c>, but the prelude is established per-statement by <c>LowerExpressionWithPrelude</c>,
/// so hoisting fires here exactly as on the [ServerExtension] path — this is NOT a no-op. Without hoisting the
/// two host calls would sit nested inside the <c>record.new</c> arguments (evaluated in field-declaration order),
/// so there would be no top-level host-call assignment statements at all and this assertion would fail.
/// </summary>
public sealed class InvokeAsyncObjectInitializerOrderTests
{
    [Fact]
    public async Task Object_initializer_members_lower_in_source_order_on_the_invoke_async_path()
    {
        var assembly = Compile(Source);
        var wire = Activator.CreateInstance(assembly.GetType("Sample.RecordingControlService", throwOnError: true)!)!;
        var controlType = assembly.GetType("DotBoxD.Kernels.Game.Plugin.Client.RemotePluginServer", throwOnError: true)!;
        var control = Activator.CreateInstance(controlType, [wire, null])!;
        var run = assembly.GetType("Sample.Usage", throwOnError: true)!
            .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;

        _ = await AwaitValueTaskResult<int>(run.Invoke(null, [control])!);

        var package = Assert.IsType<PluginPackage>(wire.GetType().GetProperty("LastPackage")!.GetValue(wire));
        var function = Assert.Single(package.Module.Functions);
        var hostCallOrder = function.Body
            .OfType<AssignmentStatement>()
            .Where(statement => statement.Value is CallExpression call &&
                                call.Name.StartsWith("host.world.get", StringComparison.Ordinal))
            .Select(statement => ((CallExpression)statement.Value).Name)
            .ToArray();

        Assert.Equal(["host.world.getB", "host.world.getA"], hostCallOrder);
    }

    private const string Source = """
        using System;
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
            public sealed class Pair
            {
                public int A { get; init; }
                public int B { get; init; }
            }
            [DotBoxDService]
            public interface IGameWorldAccess
            {
                [HostBinding("host.world.getA", "game.world.read.a", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int GetA();

                [HostBinding("host.world.getB", "game.world.read.b", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int GetB();
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
                    => await kernels.InvokeAsync(async (IGameWorldAccess world) =>
                    {
                        var pair = new Pair { B = world.GetB(), A = world.GetA() };
                        return pair.A * 10 + pair.B;
                    });
            }

            public sealed class RecordingControlService : IGamePluginControlService
            {
                public PluginPackage? LastPackage { get; private set; }

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
                    => ValueTask.FromResult(KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int32(0)));

                private ValueTask<string> InstallPackageAsync(string packageJson)
                {
                    LastPackage = DotBoxD.Plugins.Json.PluginPackageJsonSerializer.Import(packageJson);
                    return ValueTask.FromResult(LastPackage.Manifest.PluginId);
                }
            }
        }
        """;

    private static Assembly Compile(string source)
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithFeatures([
                new KeyValuePair<string, string>(
                    "InterceptorsNamespaces",
                    DotBoxDGenerationNames.TypeNames.GeneratedInterceptorsNamespace)]);
        var compilation = CSharpCompilation.Create(
            "DotBoxDInvokeAsyncInitializerOrderTest",
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
        => (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(reference => MetadataReference.CreateFromFile(reference));
}
