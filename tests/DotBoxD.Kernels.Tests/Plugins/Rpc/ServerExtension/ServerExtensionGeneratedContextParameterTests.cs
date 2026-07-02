using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class ServerExtensionGeneratedContextParameterTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Same_compilation_generated_context_parameter_is_not_part_of_wire_signature()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            SameCompilationSource,
            "Sample.ContextReadPluginPackage");

        Assert.Empty(package.Module.Functions.Single().Parameters);
        Assert.Contains("sample.read.value", package.Manifest.RequiredCapabilities);
    }

    [Fact]
    public void Prebuilt_sdk_generated_context_parameter_lowers_raw_host_calls()
    {
        var sdk = CompileSdkReference();
        var package = PluginAnalyzerGeneratedPackageFactory.CreateWithReferences(
            PrebuiltSdkUsageSource,
            "Consumer.ContextReadPluginPackage",
            sdk);

        Assert.Empty(package.Module.Functions.Single().Parameters);
        Assert.Contains("sample.read.value", package.Manifest.RequiredCapabilities);
    }

    [Fact]
    public void User_authored_context_lookalike_is_not_a_server_extension_context_parameter()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;

            namespace Sample;

            public sealed class LookalikeContext
            {
                public HookContext Raw => throw new System.NotSupportedException();
                public static LookalikeContext FromHookContext(HookContext raw) => new();
            }

            [ServerExtension("bad-context")]
            public sealed partial class BadContextKernel
            {
                public int Read(LookalikeContext ctx) => 0;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("HookContext or a generated plugin context", StringComparison.Ordinal));
    }

    private static MetadataReference CompileSdkReference()
    {
        var compilation = CreateCompilation(SdkSource, "GeneratedContextSdk");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        using var stream = new MemoryStream();
        var emit = outputCompilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static CSharpCompilation CreateCompilation(string source, string assemblyName)
        => CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(
                    typeof(DotBoxD.Services.Attributes.RpcServiceAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

    private const string SameCompilationSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace Sample
        {
            [RpcService]
            public interface IGameWorld
            {
                [HostBinding("sample.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int Read();
            }

            [GeneratePluginServer(Context = typeof(GamePluginContext))]
            public partial class GamePluginServer : IGameWorld;

            public sealed partial class GamePluginContext;
        }

        namespace Sample.Ipc
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

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static Sample.IGameWorld GetGameWorld(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new System.InvalidOperationException("not used");
            }
        }

        namespace Sample
        {
            [ServerExtension("context-read")]
            public sealed partial class ContextReadKernel
            {
                private readonly IGameWorld _world;

                public ContextReadKernel(IGameWorld world) => _world = world;

                [ServerExtensionMethod]
                public int Read(GamePluginContext ctx)
                {
                    return _world.Read();
                }
            }
        }
        """;

    private const string SdkSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Services.Attributes;

        namespace Sdk
        {
            [RpcService]
            public interface IGameWorld
            {
                [HostBinding("sample.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int Read();
            }

            [GeneratePluginServer(Context = typeof(GamePluginContext))]
            public partial class GamePluginServer : IGameWorld;

            public sealed partial class GamePluginContext;
        }

        namespace Sdk.Ipc
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

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static Sdk.IGameWorld GetGameWorld(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new System.InvalidOperationException("not used");
            }
        }
        """;

    private const string PrebuiltSdkUsageSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;
        using Sdk;

        namespace Consumer;

        [ServerExtension("context-read")]
        public sealed partial class ContextReadKernel
        {
            [ServerExtensionMethod]
            public int Read(GamePluginContext ctx)
            {
                return ctx.Raw.Host<IGameWorld>().Read();
            }
        }
        """;
}
