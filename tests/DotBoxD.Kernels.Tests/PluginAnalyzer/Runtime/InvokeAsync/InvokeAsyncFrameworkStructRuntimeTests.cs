using System.Reflection;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class InvokeAsyncFrameworkStructRuntimeTests
{
    [Fact]
    public async Task Generated_InvokeAsync_round_trips_framework_struct_capture_and_result()
    {
        var inputDate = new DateOnly(2026, 6, 28);
        var inputTime = new TimeOnly(13, 14, 15).Add(TimeSpan.FromTicks(16));
        var inputIndex = Index.FromEnd(3);
        var inputRange = new Range(Index.FromStart(2), Index.FromEnd(5));
        var responseDate = new DateOnly(2035, 7, 8);
        var responseTime = new TimeOnly(9, 10, 11).Add(TimeSpan.FromTicks(12));
        var responseIndex = Index.FromStart(4);
        var responseRange = new Range(Index.FromEnd(9), Index.FromStart(12));
        var assembly = Compile(Source);
        var wire = Activator.CreateInstance(
            assembly.GetType("Sample.RecordingControlService", throwOnError: true)!,
            [KernelRpcBinaryCodec.EncodeValue(FrameworkWireValue(responseDate, responseTime, responseIndex, responseRange))])!;
        var controlType = assembly.GetType("DotBoxD.Kernels.Game.Plugin.Client.RemotePluginServer", throwOnError: true)!;
        var control = Activator.CreateInstance(controlType, [wire, null])!;
        var method = assembly.GetType("Sample.Usage", throwOnError: true)!
            .GetMethod("EchoFramework", BindingFlags.Public | BindingFlags.Static)!;

        var result = await InvokeValueTask<object>(
            method,
            control,
            inputDate,
            inputTime,
            inputIndex,
            inputRange);

        AssertFrameworkObject(result, responseDate, responseTime, responseIndex, responseRange);
        var arguments = (byte[])wire.GetType().GetProperty("LastArguments")!.GetValue(wire)!;
        AssertFrameworkWire(
            Assert.Single(KernelRpcBinaryCodec.DecodeArguments(arguments)),
            inputDate,
            inputTime,
            inputIndex,
            inputRange);

        var invalidWire = Activator.CreateInstance(
            assembly.GetType("Sample.RecordingControlService", throwOnError: true)!,
            [KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
            [
                KernelRpcValue.Int32(responseDate.DayNumber),
                KernelRpcValue.Int64(responseTime.Ticks),
                KernelRpcValue.Record([KernelRpcValue.Int32(-1), KernelRpcValue.Bool(false)]),
                RangeWireValue(responseRange)
            ]))])!;
        var invalidControl = Activator.CreateInstance(controlType, [invalidWire, null])!;
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => InvokeValueTask<object>(method, invalidControl, inputDate, inputTime, inputIndex, inputRange));
        Assert.Contains("Index wire value", ex.Message, StringComparison.Ordinal);
    }

    private static KernelRpcValue FrameworkWireValue(DateOnly date, TimeOnly time, Index index, Range range)
        => KernelRpcValue.Record(
        [
            KernelRpcValue.Int32(date.DayNumber),
            KernelRpcValue.Int64(time.Ticks),
            IndexWireValue(index),
            RangeWireValue(range)
        ]);

    private static KernelRpcValue IndexWireValue(Index value)
        => KernelRpcValue.Record([KernelRpcValue.Int32(value.Value), KernelRpcValue.Bool(value.IsFromEnd)]);

    private static KernelRpcValue RangeWireValue(Range value)
        => KernelRpcValue.Record([IndexWireValue(value.Start), IndexWireValue(value.End)]);

    private static void AssertFrameworkWire(
        KernelRpcValue value,
        DateOnly date,
        TimeOnly time,
        Index index,
        Range range)
    {
        value.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(4, value.ItemCount);
        Assert.Equal(date.DayNumber, value.GetItem(0).Int32Value);
        Assert.Equal(time.Ticks, value.GetItem(1).Int64Value);
        AssertIndexWire(value.GetItem(2), index);
        AssertRangeWire(value.GetItem(3), range);
    }

    private static void AssertIndexWire(KernelRpcValue value, Index expected)
    {
        value.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(2, value.ItemCount);
        Assert.Equal(expected.Value, value.GetItem(0).Int32Value);
        Assert.Equal(expected.IsFromEnd, value.GetItem(1).BoolValue);
    }

    private static void AssertRangeWire(KernelRpcValue value, Range expected)
    {
        value.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(2, value.ItemCount);
        AssertIndexWire(value.GetItem(0), expected.Start);
        AssertIndexWire(value.GetItem(1), expected.End);
    }

    private static void AssertFrameworkObject(
        object value,
        DateOnly date,
        TimeOnly time,
        Index index,
        Range range)
    {
        var type = value.GetType();
        Assert.Equal(date, Assert.IsType<DateOnly>(type.GetProperty("Date")!.GetValue(value)));
        Assert.Equal(time, Assert.IsType<TimeOnly>(type.GetProperty("Time")!.GetValue(value)));
        Assert.Equal(index, Assert.IsType<Index>(type.GetProperty("Index")!.GetValue(value)));
        Assert.Equal(range, Assert.IsType<Range>(type.GetProperty("Range")!.GetValue(value)));
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
            public sealed record FrameworkCapture(DateOnly Date, TimeOnly Time, Index Index, Range Range);

            public static class Usage
            {
                public static async ValueTask<FrameworkCapture> EchoFramework(
                    RemotePluginServer kernels,
                    DateOnly date,
                    TimeOnly time,
                    Index index,
                    Range range)
                    => await kernels.InvokeAsync(
                        new FrameworkCapture(date, time, index, range),
                        async (IGameWorldAccess world, FrameworkCapture bag) =>
                        {
                            return bag;
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
            "DotBoxDInvokeAsyncFrameworkStructTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Abstractions.IServerExtensionClientRegistry).Assembly.Location))
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

    private static async Task<T> InvokeValueTask<T>(MethodInfo method, params object[] arguments)
    {
        object valueTask;
        try
        {
            valueTask = method.Invoke(null, arguments)!;
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
