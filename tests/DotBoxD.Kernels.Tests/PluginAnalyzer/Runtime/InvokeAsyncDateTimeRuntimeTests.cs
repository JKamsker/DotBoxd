using System.Reflection;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class InvokeAsyncDateTimeRuntimeTests
{
    [Fact]
    public async Task Generated_InvokeAsync_round_trips_DateTimeOffset_capture_and_result()
    {
        var input = new DateTimeOffset(2026, 6, 27, 14, 15, 16, 789, TimeSpan.FromHours(-7))
            .AddTicks(321);
        var response = new DateTimeOffset(2027, 1, 2, 3, 4, 5, 6, TimeSpan.FromMinutes(345))
            .AddTicks(987);

        var (result, arguments) = await InvokeAsyncReturns<DateTimeOffset>(
            "EchoOffset",
            input,
            DateTimeWireValue(response));

        Assert.Equal(response, result);
        Assert.Equal(response.Offset, result.Offset);
        var capture = Assert.Single(arguments);
        capture.RequireKind(KernelRpcValueKind.Record);
        AssertDateTimeWire(Assert.Single(capture.Items), input.UtcTicks, input.Offset.Ticks);
    }

    [Fact]
    public async Task Generated_InvokeAsync_round_trips_DateTime_capture_and_result()
    {
        var input = new DateTime(2026, 6, 27, 14, 15, 16, 789, DateTimeKind.Unspecified)
            .AddTicks(321);
        var response = new DateTime(2027, 1, 2, 3, 4, 5, 6, DateTimeKind.Unspecified)
            .AddTicks(987);

        var (result, arguments) = await InvokeAsyncReturns<DateTime>(
            "EchoDate",
            input,
            DateTimeWireValue(response));

        Assert.Equal(response, result);
        var capture = Assert.Single(arguments);
        capture.RequireKind(KernelRpcValueKind.Record);
        AssertDateTimeWire(Assert.Single(capture.Items), input.Ticks, 0L);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    public async Task Generated_InvokeAsync_rejects_DateTime_capture_with_non_unspecified_kind(DateTimeKind kind)
    {
        var input = new DateTime(2026, 6, 27, 14, 15, 16, 789, kind);
        var response = new DateTime(2027, 1, 2, 3, 4, 5, 6, DateTimeKind.Unspecified);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => InvokeAsyncReturns<DateTime>("EchoDate", input, DateTimeWireValue(response)));

        Assert.Contains("DateTimeKind.Unspecified", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generated_InvokeAsync_round_trips_TimeSpan_capture_and_result()
    {
        var input = TimeSpan.FromTicks(1_234_567_890L);
        var response = TimeSpan.FromTicks(-9_876_543_210L);

        var (result, arguments) = await InvokeAsyncReturns<TimeSpan>(
            "EchoDuration",
            input,
            TimeSpanWireValue(response));

        Assert.Equal(response, result);
        var capture = Assert.Single(arguments);
        capture.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(input.Ticks, Assert.Single(capture.Items).Int64Value);
    }

    private static async Task<(T Result, KernelRpcValue[] Arguments)> InvokeAsyncReturns<T>(
        string methodName,
        object input,
        KernelRpcValue response)
    {
        var assembly = Compile(Source);
        var wire = Activator.CreateInstance(
            assembly.GetType("Sample.RecordingControlService", throwOnError: true)!,
            [KernelRpcBinaryCodec.EncodeValue(response)])!;
        var controlType = assembly.GetType("DotBoxD.Kernels.Game.Plugin.Client.RemotePluginServer", throwOnError: true)!;
        var control = Activator.CreateInstance(controlType, [wire, null])!;
        var method = assembly.GetType("Sample.Usage", throwOnError: true)!
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!;

        var result = await InvokeValueTask<T>(method, control, input);
        var arguments = (byte[])wire.GetType().GetProperty("LastArguments")!.GetValue(wire)!;
        return (result, KernelRpcBinaryCodec.DecodeArguments(arguments));
    }

    private static KernelRpcValue DateTimeWireValue(DateTimeOffset value)
        => KernelRpcValue.Record([KernelRpcValue.Int64(value.UtcTicks), KernelRpcValue.Int64(value.Offset.Ticks)]);

    private static KernelRpcValue DateTimeWireValue(DateTime value)
        => KernelRpcValue.Record([KernelRpcValue.Int64(value.Ticks), KernelRpcValue.Int64(0L)]);

    private static KernelRpcValue TimeSpanWireValue(TimeSpan value)
        => KernelRpcValue.Int64(value.Ticks);

    private static void AssertDateTimeWire(KernelRpcValue value, long utcTicks, long offsetTicks)
    {
        value.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(2, value.ItemCount);
        Assert.Equal(utcTicks, value.GetItem(0).Int64Value);
        Assert.Equal(offsetTicks, value.GetItem(1).Int64Value);
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
            [RpcService]
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
            public sealed record OffsetCapture(DateTimeOffset Value);

            public sealed record DateCapture(DateTime Value);

            public sealed record DurationCapture(TimeSpan Value);

            public static class Usage
            {
                public static async ValueTask<DateTimeOffset> EchoOffset(RemotePluginServer kernels, DateTimeOffset value)
                    => await kernels.InvokeAsync(new OffsetCapture(value), async (IGameWorldAccess world, OffsetCapture bag) =>
                    {
                        return bag.Value;
                    });

                public static async ValueTask<DateTime> EchoDate(RemotePluginServer kernels, DateTime value)
                    => await kernels.InvokeAsync(new DateCapture(value), async (IGameWorldAccess world, DateCapture bag) =>
                    {
                        return bag.Value;
                    });

                public static async ValueTask<TimeSpan> EchoDuration(RemotePluginServer kernels, TimeSpan value)
                    => await kernels.InvokeAsync(new DurationCapture(value), async (IGameWorldAccess world, DurationCapture bag) =>
                    {
                        return bag.Value;
                    });
            }

            public sealed class RecordingControlService(byte[] response) : IGamePluginControlService
            {
                public byte[] LastArguments { get; private set; } = Array.Empty<byte>();

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
                    LastArguments = arguments;
                    return ValueTask.FromResult(response);
                }

                private static ValueTask<string> InstallPackageAsync(string packageJson)
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
            .WithFeatures([
                new KeyValuePair<string, string>(
                    "InterceptorsNamespaces",
                    DotBoxDGenerationNames.TypeNames.GeneratedInterceptorsNamespace)
            ]);

        var compilation = CSharpCompilation.Create(
            "DotBoxDInvokeAsyncDateTimeTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Kernels.SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Attributes.RpcServiceAttribute).Assembly.Location))
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

    private static async Task<T> InvokeValueTask<T>(MethodInfo method, object control, object input)
    {
        object valueTask;
        try
        {
            valueTask = method.Invoke(null, [control, input])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        var task = (Task)asTask.Invoke(valueTask, null)!;
        await task.ConfigureAwait(false);
        return (T)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        => (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(reference => MetadataReference.CreateFromFile(reference));
}
