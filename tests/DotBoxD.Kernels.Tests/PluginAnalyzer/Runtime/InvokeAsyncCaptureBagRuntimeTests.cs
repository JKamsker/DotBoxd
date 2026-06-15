using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class InvokeAsyncCaptureBagRuntimeTests
{
    [Fact]
    public async Task Generated_interceptor_round_trips_explicit_capture_bag_sync_out()
    {
        var assembly = Compile(Source);
        var wire = Activator.CreateInstance(assembly.GetType("Sample.RecordingWireClient", throwOnError: true)!)!;
        var controlType = assembly.GetType("DotBoxD.Kernels.Game.Plugin.Client.RemotePluginServer", throwOnError: true)!;
        var control = Activator.CreateInstance(controlType, [wire])!;
        var captureType = assembly.GetType("Sample.MonsterCapture", throwOnError: true)!;
        var captures = Activator.CreateInstance(captureType)!;
        captureType.GetProperty("MonsterId")!.SetValue(captures, "monster-2");

        var run = assembly.GetType("Sample.Usage", throwOnError: true)!
            .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
        var result = await AwaitValueTaskResult<string>(run.Invoke(null, [control, captures])!);

        Assert.Equal("monster-2", result);
        Assert.Equal(80, captureType.GetProperty("LastHealth")!.GetValue(captures));
        Assert.Equal("monster-2", wire.GetType().GetProperty("CapturedMonsterId")!.GetValue(wire));
        Assert.Equal(0, wire.GetType().GetProperty("CapturedLastHealth")!.GetValue(wire));

        var package = Assert.IsType<PluginPackage>(controlType.GetProperty("LastPackage")!.GetValue(control));
        Assert.Contains("game.world.monster.read.snapshot", package.Manifest.RequiredCapabilities);
        var function = Assert.Single(package.Module.Functions);
        Assert.Single(function.Parameters);
        Assert.Equal(SandboxType.Record([SandboxType.String, SandboxType.I32]), function.Parameters[0].Type);
        Assert.Equal(SandboxType.Record([SandboxType.String, SandboxType.I32]), function.ReturnType);
    }

    private const string Source = """
        using System;
        using System.Threading;
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
                public RemotePluginServer(IServerExtensionWireClient wireClient)
                    => Services = new RemoteServiceControl(wireClient);

                public RemoteServiceControl Services { get; }

                public PluginPackage? LastPackage => Services.LastPackage;

                public ValueTask<T> InvokeAsync<TCaptures, T>(
                    TCaptures captures,
                    RemoteServerInvocation<TCaptures, T> lambda)
                    where TCaptures : class
                    => throw new InvalidOperationException("not intercepted");
            }

            public sealed class RemoteServiceControl
            {
                public RemoteServiceControl(IServerExtensionWireClient wireClient) => WireClient = wireClient;

                public IServerExtensionWireClient WireClient { get; }

                public PluginPackage? LastPackage { get; private set; }

                public Task<string> EnsureAnonymousKernelAsync(string pluginId, Func<PluginPackage> packageFactory)
                {
                    LastPackage = packageFactory();
                    return Task.FromResult(pluginId);
                }
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
                public static async ValueTask<string> Run(
                    RemotePluginServer kernels,
                    MonsterCapture captures)
                    => await kernels.InvokeAsync(captures, (IGameWorldAccess world, MonsterCapture bag) =>
                    {
                        var monster = world.GetMonster(bag.MonsterId);
                        bag.LastHealth = monster.Health;
                        return monster.Name;
                    });
            }

            public sealed class RecordingWireClient : IServerExtensionWireClient
            {
                public string CapturedMonsterId { get; private set; } = "";
                public int CapturedLastHealth { get; private set; }

                public ValueTask<byte[]> InvokeServerExtensionAsync(
                    string pluginId,
                    byte[] arguments,
                    CancellationToken cancellationToken = default)
                {
                    var capture = KernelRpcBinaryCodec.DecodeArguments(arguments)[0];
                    capture.RequireKind(KernelRpcValueKind.Record);
                    CapturedMonsterId = capture.Items[0].TextValue;
                    CapturedLastHealth = capture.Items[1].Int32Value;
                    return ValueTask.FromResult(KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
                    [
                        KernelRpcValue.String("monster-2"),
                        KernelRpcValue.Int32(80)
                    ])));
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
            "DotBoxDInvokeAsyncCaptureBagRuntimeTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
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
