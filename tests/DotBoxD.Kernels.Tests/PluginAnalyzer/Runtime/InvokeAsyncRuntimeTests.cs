using System.Reflection;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class InvokeAsyncRuntimeTests
{
    [Fact]
    public async Task Generated_interceptor_installs_invokes_and_decodes_a_no_capture_lambda()
    {
        var assembly = Compile(Source, enableInterceptors: true);
        var wire = Activator.CreateInstance(assembly.GetType("Sample.RecordingWireClient", throwOnError: true)!)!;
        var controlType = assembly.GetType("DotBoxD.Kernels.Game.Plugin.Client.RemoteKernelControl", throwOnError: true)!;
        var control = Activator.CreateInstance(controlType, [wire])!;
        var run = assembly.GetType("Sample.Usage", throwOnError: true)!
            .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;

        var result = await AwaitValueTaskResult<int>(run.Invoke(null, [control])!);

        Assert.Equal(42, result);
        Assert.Equal(1, (int)controlType.GetProperty("InstallCount")!.GetValue(control)!);
        Assert.Equal(0, (int)wire.GetType().GetProperty("ArgumentCount")!.GetValue(wire)!);

        var package = Assert.IsType<PluginPackage>(controlType.GetProperty("LastPackage")!.GetValue(control));
        Assert.StartsWith("$anon:", package.Manifest.PluginId, StringComparison.Ordinal);
        Assert.Contains("game.world.monster.read.health", package.Manifest.RequiredCapabilities);
    }

    [Fact]
    public async Task Generated_interceptor_package_lowers_snapshot_member_access_to_record_get()
    {
        var assembly = Compile(ObjectSurfaceSource, enableInterceptors: true);
        var wire = Activator.CreateInstance(assembly.GetType("Sample.RecordingWireClient", throwOnError: true)!)!;
        var controlType = assembly.GetType("DotBoxD.Kernels.Game.Plugin.Client.RemoteKernelControl", throwOnError: true)!;
        var control = Activator.CreateInstance(controlType, [wire])!;
        var run = assembly.GetType("Sample.Usage", throwOnError: true)!
            .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;

        var result = await AwaitValueTaskResult<int>(run.Invoke(null, [control])!);

        Assert.Equal(80, result);
        var package = Assert.IsType<PluginPackage>(controlType.GetProperty("LastPackage")!.GetValue(control));
        Assert.Contains("game.world.monster.read.snapshot", package.Manifest.RequiredCapabilities);
        Assert.Equal(SandboxType.I32, Assert.Single(package.Module.Functions).ReturnType);

        var function = Assert.Single(package.Module.Functions);
        var assignment = Assert.IsType<AssignmentStatement>(function.Body[0]);
        Assert.Equal("monster", assignment.Name);
        Assert.Equal("host.world.getMonster", Assert.IsType<CallExpression>(assignment.Value).Name);

        var returned = Assert.IsType<ReturnStatement>(function.Body[1]);
        var recordGet = Assert.IsType<CallExpression>(returned.Value);
        Assert.Equal("record.get", recordGet.Name);
        Assert.Equal("monster", Assert.IsType<VariableExpression>(recordGet.Arguments[0]).Name);
        Assert.Equal(2, Assert.IsType<I32Value>(Assert.IsType<LiteralExpression>(recordGet.Arguments[1]).Value).Value);
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
            public interface IGameWorldAccess
            {
                [HostBinding("host.world.getHealth", "game.world.monster.read.health", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int GetHealth(string entityId);
            }
        }

        namespace DotBoxD.Kernels.Game.Plugin.Client
        {
            public sealed class RemoteKernelControl
            {
                public RemoteKernelControl(IKernelRpcWireClient wireClient) => WireClient = wireClient;

                public IKernelRpcWireClient WireClient { get; }

                public int InstallCount { get; private set; }

                public PluginPackage? LastPackage { get; private set; }

                public Task<string> EnsureAnonymousKernelAsync(string pluginId, Func<PluginPackage> packageFactory)
                {
                    InstallCount++;
                    LastPackage = packageFactory();
                    return Task.FromResult(pluginId);
                }

                public ValueTask<T> InvokeAsync<T>(Func<IGameWorldAccess, T> lambda)
                    => throw new InvalidOperationException("not intercepted");
            }
        }

        namespace Sample
        {
            public static class Usage
            {
                public static async ValueTask<int> Run(RemoteKernelControl kernels)
                    => await kernels.InvokeAsync((IGameWorldAccess world) =>
                    {
                        var hp = world.GetHealth("monster-1");
                        return hp;
                    });
            }

            public sealed class RecordingWireClient : IKernelRpcWireClient
            {
                public int ArgumentCount { get; private set; }

                public ValueTask<byte[]> InvokeKernelRpcAsync(
                    string pluginId,
                    byte[] arguments,
                    CancellationToken cancellationToken = default)
                {
                    ArgumentCount = KernelRpcBinaryCodec.DecodeArguments(arguments).Length;
                    return ValueTask.FromResult(KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int32(42)));
                }
            }
        }
        """;

    private const string ObjectSurfaceSource = """
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
            public sealed class RemoteKernelControl
            {
                public RemoteKernelControl(IKernelRpcWireClient wireClient) => WireClient = wireClient;

                public IKernelRpcWireClient WireClient { get; }

                public PluginPackage? LastPackage { get; private set; }

                public Task<string> EnsureAnonymousKernelAsync(string pluginId, Func<PluginPackage> packageFactory)
                {
                    LastPackage = packageFactory();
                    return Task.FromResult(pluginId);
                }

                public ValueTask<T> InvokeAsync<T>(Func<IGameWorldAccess, T> lambda)
                    => throw new InvalidOperationException("not intercepted");
            }
        }

        namespace Sample
        {
            public static class Usage
            {
                public static async ValueTask<int> Run(RemoteKernelControl kernels)
                    => await kernels.InvokeAsync((IGameWorldAccess world) =>
                    {
                        var monster = world.GetMonster("monster-2");
                        return monster.Health;
                    });
            }

            public sealed class RecordingWireClient : IKernelRpcWireClient
            {
                public ValueTask<byte[]> InvokeKernelRpcAsync(
                    string pluginId,
                    byte[] arguments,
                    CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int32(80)));
            }
        }
        """;

    private static Assembly Compile(string source, bool enableInterceptors)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (enableInterceptors)
        {
            parseOptions = parseOptions.WithFeatures(
                [
                    new KeyValuePair<string, string>(
                        "InterceptorsNamespaces",
                        DotBoxDGenerationNames.TypeNames.GeneratedInterceptorsNamespace)
                ]);
        }

        var compilation = CSharpCompilation.Create(
            "DotBoxDInvokeAsyncRuntimeTest",
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
