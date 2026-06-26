using System.Reflection;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class InvokeAsyncScalarDecodeRuntimeTests
{
    [Fact]
    public async Task Generated_InvokeAsync_rejects_float_overflow_result()
        => await AssertInvokeAsyncRejects(
            "ReadFloat",
            KernelRpcValue.Double(double.MaxValue));

    [Fact]
    public async Task Generated_InvokeAsync_rejects_narrow_enum_overflow_result()
        => await AssertInvokeAsyncRejects(
            "ReadSmall",
            KernelRpcValue.Int32(300));

    [Fact]
    public async Task Generated_InvokeAsync_rejects_float_overflow_inside_dto_result()
        => await AssertInvokeAsyncRejects(
            "ReadFloatDto",
            KernelRpcValue.Record([KernelRpcValue.Double(double.MaxValue)]));

    [Fact]
    public async Task Generated_InvokeAsync_rejects_narrow_enum_overflow_inside_dto_result()
        => await AssertInvokeAsyncRejects(
            "ReadEnumDto",
            KernelRpcValue.Record([KernelRpcValue.Int32(300)]));

    private static async Task AssertInvokeAsyncRejects(string methodName, KernelRpcValue response)
    {
        var assembly = Compile(Source, enableInterceptors: true);
        var wire = Activator.CreateInstance(
            assembly.GetType("Sample.RecordingControlService", throwOnError: true)!,
            [KernelRpcBinaryCodec.EncodeValue(response)])!;
        var controlType = assembly.GetType("DotBoxD.Kernels.Game.Plugin.Client.RemotePluginServer", throwOnError: true)!;
        var control = Activator.CreateInstance(controlType, [wire, null])!;
        var method = assembly.GetType("Sample.Usage", throwOnError: true)!
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!;

        await Assert.ThrowsAsync<NotSupportedException>(() => InvokeValueTask(method, control));
    }

    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Game.Plugin.Client;
        using DotBoxD.Kernels.Game.Server.Abstractions;
        using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace DotBoxD.Kernels.Game.Server.Abstractions
        {
            [DotBoxDService]
            public interface IGameWorldAccess;
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
                ValueTask UpdateSettingsAsync(string pluginId, LiveSettingUpdate[] updates, bool atomic = false, CancellationToken ct = default);
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
            public enum Small : byte
            {
                Zero = 0,
                FortyFour = 44
            }

            public sealed record FloatDto(float Value);

            public sealed record EnumDto(Small Value);

            public static class Usage
            {
                public static async ValueTask<float> ReadFloat(RemotePluginServer kernels)
                    => await kernels.InvokeAsync(async (IGameWorldAccess world) => { return 1.5f; });

                public static async ValueTask<Small> ReadSmall(RemotePluginServer kernels)
                    => await kernels.InvokeAsync(async (IGameWorldAccess world) => { return Small.Zero; });

                public static async ValueTask<FloatDto> ReadFloatDto(RemotePluginServer kernels)
                    => await kernels.InvokeAsync(async (IGameWorldAccess world) => { return new FloatDto(1.5f); });

                public static async ValueTask<EnumDto> ReadEnumDto(RemotePluginServer kernels)
                    => await kernels.InvokeAsync(async (IGameWorldAccess world) => { return new EnumDto(Small.Zero); });
            }

            public sealed class RecordingControlService(byte[] response) : IGamePluginControlService
            {
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
                    => ValueTask.FromResult(response);

                private static ValueTask<string> InstallPackageAsync(string packageJson)
                {
                    var package = DotBoxD.Plugins.Json.PluginPackageJsonSerializer.Import(packageJson);
                    return ValueTask.FromResult(package.Manifest.PluginId);
                }
            }
        }
        """;

    private static Assembly Compile(string source, bool enableInterceptors)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (enableInterceptors)
        {
            parseOptions = parseOptions.WithFeatures([
                new KeyValuePair<string, string>("InterceptorsNamespaces", DotBoxDGenerationNames.TypeNames.GeneratedInterceptorsNamespace)]);
        }

        var compilation = CSharpCompilation.Create(
            "DotBoxDInvokeAsyncScalarDecodeTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Kernels.SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Attributes.DotBoxDServiceAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Peer.RpcPeerSession).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Pushdown.Services.RpcMessagePackIpc).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new PluginPackageGenerator().AsSourceGenerator()], parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static async Task InvokeValueTask(MethodInfo method, object control)
    {
        object valueTask;
        try
        {
            valueTask = method.Invoke(null, [control])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        var task = (Task)asTask.Invoke(valueTask, null)!;
        await task.ConfigureAwait(false);
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        => (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(reference => MetadataReference.CreateFromFile(reference));
}
