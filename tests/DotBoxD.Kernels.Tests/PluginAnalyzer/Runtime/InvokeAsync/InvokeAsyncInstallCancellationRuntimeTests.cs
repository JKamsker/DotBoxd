using System.Reflection;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class InvokeAsyncInstallCancellationRuntimeTests
{
    [Fact]
    public async Task Caller_cancellation_does_not_cancel_the_shared_anonymous_kernel_install()
    {
        var assembly = Compile(Source);
        var wire = Activator.CreateInstance(
            assembly.GetType("Sample.RecordingControlService", throwOnError: true)!)!;
        var controlType = assembly.GetType(
            "DotBoxD.Kernels.Game.Plugin.Client.RemotePluginServer",
            throwOnError: true)!;
        var control = Activator.CreateInstance(controlType, [wire, null])!;
        var run = assembly.GetType("Sample.Usage", throwOnError: true)!
            .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;

        using var firstCts = new CancellationTokenSource();
        var first = InvokeRun(run, control, firstCts.Token);
        await ((TaskCompletionSource)wire.GetType().GetProperty("InstallStarted")!.GetValue(wire)!)
            .Task;

        firstCts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await first);

        ((TaskCompletionSource)wire.GetType().GetProperty("ReleaseInstall")!.GetValue(wire)!).SetResult();
        var second = await InvokeRun(run, control, CancellationToken.None);

        Assert.Equal(42, second);
        Assert.Equal(1, wire.GetType().GetProperty("InstallAttempts")!.GetValue(wire));
        Assert.Equal(0, wire.GetType().GetProperty("CanceledInstallAttempts")!.GetValue(wire));
    }

    [Fact]
    public async Task Canceled_caller_does_not_cache_later_failed_anonymous_kernel_install()
    {
        var assembly = Compile(Source);
        var wire = Activator.CreateInstance(
            assembly.GetType("Sample.RecordingControlService", throwOnError: true)!)!;
        var controlType = assembly.GetType(
            "DotBoxD.Kernels.Game.Plugin.Client.RemotePluginServer",
            throwOnError: true)!;
        var control = Activator.CreateInstance(controlType, [wire, null])!;
        var run = assembly.GetType("Sample.Usage", throwOnError: true)!
            .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;

        using var firstCts = new CancellationTokenSource();
        var first = InvokeRun(run, control, firstCts.Token);
        await ((TaskCompletionSource)wire.GetType().GetProperty("InstallStarted")!.GetValue(wire)!)
            .Task;

        firstCts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await first);

        wire.GetType().GetProperty("FailNextInstallAfterRelease")!.SetValue(wire, true);
        ((TaskCompletionSource)wire.GetType().GetProperty("ReleaseInstall")!.GetValue(wire)!).SetResult();
        var second = await InvokeRun(run, control, CancellationToken.None);

        Assert.Equal(42, second);
        Assert.Equal(2, wire.GetType().GetProperty("InstallAttempts")!.GetValue(wire));
        Assert.Equal(0, wire.GetType().GetProperty("CanceledInstallAttempts")!.GetValue(wire));
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
                public static async ValueTask<int> Run(RemotePluginServer kernels, CancellationToken ct)
                    => await kernels.InvokeAsync(async (IGameWorldAccess world) =>
                    {
                        return 1;
                    }, ct);
            }

            public sealed class RecordingControlService : IGamePluginControlService
            {
                private int _installAttempts;
                private int _canceledInstallAttempts;

                public TaskCompletionSource InstallStarted { get; } =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);

                public TaskCompletionSource ReleaseInstall { get; } =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);

                public int InstallAttempts => _installAttempts;

                public int CanceledInstallAttempts => _canceledInstallAttempts;

                public bool FailNextInstallAfterRelease { get; set; }

                public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
                    => InstallPackageAsync(packageJson, ct);

                public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default)
                    => InstallPackageAsync(packageJson, ct);

                public ValueTask<string> InstallServerExtensionAsync(
                    string packageJson,
                    CancellationToken ct = default)
                    => InstallPackageAsync(packageJson, ct);

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
                    => ValueTask.FromResult(KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int32(42)));

                private async ValueTask<string> InstallPackageAsync(string packageJson, CancellationToken ct)
                {
                    var package = DotBoxD.Plugins.Json.PluginPackageJsonSerializer.Import(packageJson);
                    Interlocked.Increment(ref _installAttempts);
                    InstallStarted.TrySetResult();

                    try
                    {
                        await ReleaseInstall.Task.WaitAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        Interlocked.Increment(ref _canceledInstallAttempts);
                        throw;
                    }

                    if (FailNextInstallAfterRelease)
                    {
                        FailNextInstallAfterRelease = false;
                        throw new InvalidOperationException("detached install failed");
                    }

                    return package.Manifest.PluginId;
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
            "DotBoxDInvokeAsyncInstallCancellationTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Kernels.SandboxModule).Assembly.Location))
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

    private static async Task<int> InvokeRun(MethodInfo method, object control, CancellationToken cancellationToken)
    {
        object valueTask;
        try
        {
            valueTask = method.Invoke(null, [control, cancellationToken])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        return await ((Task<int>)asTask.Invoke(valueTask, null)!).ConfigureAwait(false);
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        => (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(reference => MetadataReference.CreateFromFile(reference));
}
