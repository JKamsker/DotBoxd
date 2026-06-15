using System.Reflection;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using KernelSemVersion = DotBoxD.Kernels.Model.SemVersion;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class InvokeAsyncImplicitCaptureRuntimeTests
{
    [Fact]
    public async Task Generated_interceptor_round_trips_implicit_capture_reflection_sync_out()
    {
        var assembly = Compile(Source);
        var wire = Activator.CreateInstance(assembly.GetType("Sample.RecordingWireClient", throwOnError: true)!)!;
        var controlType = assembly.GetType("DotBoxD.Kernels.Game.Plugin.Client.RemotePluginServer", true)!;
        var control = Activator.CreateInstance(controlType, [wire])!;
        var run = assembly.GetType("Sample.Usage", true)!
            .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;

        var result = await AwaitValueTaskResult<string>(run.Invoke(null, [control])!);

        Assert.Equal("monster-2:80", result);
        Assert.Equal("monster-2", wire.GetType().GetProperty("CapturedMonsterId")!.GetValue(wire));
        Assert.Equal(0, wire.GetType().GetProperty("CapturedLastHealth")!.GetValue(wire));

        var package = Assert.IsType<PluginPackage>(controlType.GetProperty("LastPackage")!.GetValue(control));
        var function = Assert.Single(package.Module.Functions);
        Assert.Equal(2, function.Parameters.Count);
        Assert.Equal(SandboxType.String, function.Parameters[0].Type);
        Assert.Equal(SandboxType.I32, function.Parameters[1].Type);
        Assert.Equal(SandboxType.Record([SandboxType.String, SandboxType.I32]), function.ReturnType);

        var realResult = await ExecuteGeneratedPackageAsync(package);
        Assert.Equal([SandboxValue.FromString("monster-2"), SandboxValue.FromInt32(80)], realResult.Fields);
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
            public sealed class RemotePluginServer
            {
                public RemotePluginServer(IServerExtensionWireClient wireClient)
                    => Services = new RemoteServiceControl(wireClient);

                public RemoteServiceControl Services { get; }

                public PluginPackage? LastPackage => Services.LastPackage;

                public ValueTask<T> InvokeAsync<T>(Func<IGameWorldAccess, T> lambda)
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
            public static class Usage
            {
                public static async ValueTask<string> Run(RemotePluginServer kernels)
                {
                    var monsterId = "monster-2";
                    var lastHealth = 0;
                    var name = await kernels.InvokeAsync((IGameWorldAccess world) =>
                    {
                        var monster = world.GetMonster(monsterId);
                        lastHealth = monster.Health;
                        return monster.Name;
                    });

                    return name + ":" + lastHealth;
                }
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
                    var values = KernelRpcBinaryCodec.DecodeArguments(arguments);
                    CapturedMonsterId = values[0].TextValue;
                    CapturedLastHealth = values[1].Int32Value;
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
            "DotBoxDInvokeAsyncImplicitCaptureRuntimeTest",
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

    private static async Task<RecordValue> ExecuteGeneratedPackageAsync(PluginPackage package)
    {
        using var server = PluginServer.Create(
            configureHost: AddMonsterBinding,
            defaultPolicy: MonsterReadPolicy(),
            executionMode: ExecutionMode.Compiled);
        var kernel = await server.InstallServerExtensionAsync(package);
        var result = await kernel.InvokeServerExtensionAsync(
        [
            SandboxValue.FromString("monster-2"),
            SandboxValue.FromInt32(0)
        ]);

        return Assert.IsType<RecordValue>(result);
    }

    private static void AddMonsterBinding(SandboxHostBuilder builder)
        => builder.AddBinding(new BindingDescriptor(
            "host.world.getMonster",
            KernelSemVersion.One,
            [SandboxType.String],
            MonsterSnapshotType(),
            SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead,
            "game.world.monster.read.snapshot",
            BindingCostModel.Fixed(3),
            AuditLevel.PerResource,
            BindingSafety.ReadOnlyExternal,
            (context, args, _) =>
            {
                var startedAt = DateTimeOffset.UtcNow;
                var entityId = ((StringValue)args[0]).Value;
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    startedAt,
                    true,
                    BindingId: "host.world.getMonster",
                    CapabilityId: "game.world.monster.read.snapshot",
                    Effect: SandboxEffect.HostStateRead,
                    ResourceId: $"entity:{entityId}",
                    Fields: context.BindingAuditFields("game-world", startedAt)));
                return ValueTask.FromResult(SandboxValue.FromRecord(
                [
                    SandboxValue.FromString(entityId),
                    SandboxValue.FromString(entityId),
                    SandboxValue.FromInt32(80),
                    SandboxValue.FromInt32(8),
                    SandboxValue.FromInt32(12)
                ]));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { }));

    private static SandboxType MonsterSnapshotType()
        => SandboxType.Record(
        [
            SandboxType.String,
            SandboxType.String,
            SandboxType.I32,
            SandboxType.I32,
            SandboxType.I32
        ]);

    private static SandboxPolicy MonsterReadPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .Grant("game.world.monster.read.*", new { }, SandboxEffect.HostStateRead)
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
