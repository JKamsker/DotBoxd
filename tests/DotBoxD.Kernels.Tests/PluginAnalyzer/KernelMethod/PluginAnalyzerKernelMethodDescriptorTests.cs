using System.Reflection;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Policies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

public sealed partial class PluginAnalyzerKernelMethodDescriptorTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);

    [Fact]
    public void Generated_sdk_emits_multiple_context_kernel_method_descriptors()
    {
        var (result, _) = CompileSdk(PluginAnalyzerKernelMethodDescriptorTestSources.Sdk);
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Equal(2, Count(generated, "GeneratedKernelMethodDescriptorAttribute"));
        Assert.Contains("IsAllowed", generated, StringComparison.Ordinal);
        Assert.Contains("IsClose", generated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prebuilt_sdk_context_kernel_method_descriptor_lowers_and_runs()
    {
        var (_, sdkReference) = CompileSdk(PluginAnalyzerKernelMethodDescriptorTestSources.Sdk);
        var assembly = CompileGeneratedAssembly(PluginAnalyzerKernelMethodDescriptorTestSources.Consumer, sdkReference);
        var package = HookChainPackage(assembly);

        Assert.Contains("sample.read.value", package.Manifest.RequiredCapabilities);
        Assert.Contains("host.message.write", package.Manifest.RequiredCapabilities);

        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(
            messages,
            configureHost: AddReadBinding,
            defaultPolicy: ReadPolicy());
        server.Hooks.On<KernelMethodAggroEvent>().UseGeneratedChain(package);

        await server.Hooks.PublishAsync(new KernelMethodAggroEvent("monster-1", 3, 10, 5));
        await server.Hooks.PublishAsync(new KernelMethodAggroEvent("monster-2", 9, 10, 5));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("monster-1", message.TargetId);
    }

    [Fact]
    public async Task Prebuilt_sdk_inherited_context_kernel_method_descriptor_lowers_and_runs()
    {
        var (result, sdkReference) = CompileSdk(PluginAnalyzerKernelMethodDescriptorTestSources.InheritedSdk);
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Equal(1, Count(generated, "GeneratedKernelMethodDescriptorAttribute"));
        Assert.Contains("global::Sdk.BaseGamePluginContext", generated, StringComparison.Ordinal);

        var assembly = CompileGeneratedAssembly(
            PluginAnalyzerKernelMethodDescriptorTestSources.InheritedConsumer,
            sdkReference);
        var package = HookChainPackage(assembly);

        Assert.Contains("host.message.write", package.Manifest.RequiredCapabilities);

        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(messages, defaultPolicy: ReadPolicy());
        server.Hooks.On<KernelMethodAggroEvent>().UseGeneratedChain(package);

        await server.Hooks.PublishAsync(new KernelMethodAggroEvent("monster-1", 3, 10, 5));
        await server.Hooks.PublishAsync(new KernelMethodAggroEvent("monster-2", 9, 10, 5));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("monster-1", message.TargetId);
    }

    [Fact]
    public void Metadata_only_context_kernel_method_without_descriptor_is_rejected()
    {
        var sdkReference = CompilePlainReference(
            PluginAnalyzerKernelMethodDescriptorTestSources.DescriptorlessSdk,
            "DescriptorlessSdk");
        var diagnostics = GeneratorDiagnostics(
            PluginAnalyzerKernelMethodDescriptorTestSources.DescriptorlessConsumer,
            sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "requires a matching generated descriptor",
                StringComparison.Ordinal));
    }

    private static (GeneratorDriverRunResult Result, MetadataReference Reference) CompileSdk(string source)
    {
        var compilation = CreateCompilation(source, "GeneratedKernelMethodDescriptorSdk");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var reference = EmitReference(outputCompilation);
        return (driver.GetRunResult(), reference);
    }

    private static MetadataReference CompilePlainReference(string source, string assemblyName)
    {
        var compilation = CreateCompilation(source, assemblyName);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        return EmitReference(compilation);
    }

    private static IReadOnlyList<Diagnostic> GeneratorDiagnostics(string source, params MetadataReference[] references)
    {
        var compilation = CreateCompilation(source, "DescriptorlessConsumer", references);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);
        return generatorDiagnostics.Concat(outputCompilation.GetDiagnostics()).ToArray();
    }

    private static Assembly CompileGeneratedAssembly(string source, MetadataReference reference)
    {
        var compilation = CreateCompilation(source, "GeneratedKernelMethodDescriptorConsumer", reference);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        using var stream = new MemoryStream();
        var emit = outputCompilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static MetadataReference EmitReference(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        var bytes = stream.ToArray();
        Assembly.Load(bytes);
        return MetadataReference.CreateFromImage(bytes);
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        string assemblyName,
        params MetadataReference[] references)
        => CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Attributes.RpcServiceAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(KernelMethodAggroEvent).Assembly.Location))
                .Concat(references),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static PluginPackage HookChainPackage(Assembly assembly)
    {
        var packageType = assembly.GetTypes().Single(type =>
            type.Name.StartsWith("HookChain_", StringComparison.Ordinal) &&
            type.Name.EndsWith("PluginPackage", StringComparison.Ordinal));
        return (PluginPackage)packageType
            .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;
    }

    private static void AddReadBinding(SandboxHostBuilder builder)
        => builder.AddBinding(new BindingDescriptor(
            "host.Sdk.IGameWorld.Read",
            SemVersion.One,
            [SandboxType.String],
            SandboxType.I32,
            SandboxEffect.Cpu | SandboxEffect.HostStateRead,
            "sample.read.value",
            BindingCostModel.Fixed(2),
            AuditLevel.PerResource,
            BindingSafety.ReadOnlyExternal,
            (context, args, _) =>
            {
                var startedAt = DateTimeOffset.UtcNow;
                var id = ((StringValue)args[0]).Value;
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    startedAt,
                    true,
                    BindingId: "host.Sdk.IGameWorld.Read",
                    CapabilityId: "sample.read.value",
                    Effect: SandboxEffect.HostStateRead,
                    ResourceId: id,
                    Fields: context.BindingAuditFields("sample", startedAt)));
                return ValueTask.FromResult(SandboxValue.FromInt32(42));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { }));

    private static SandboxPolicy ReadPolicy()
        => SandboxPolicyBuilder.Create().GrantLogging()
            .GrantHostMessageWrite()
            .Grant("sample.read.*", new { }, SandboxEffect.HostStateRead)
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private static int Count(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

}
