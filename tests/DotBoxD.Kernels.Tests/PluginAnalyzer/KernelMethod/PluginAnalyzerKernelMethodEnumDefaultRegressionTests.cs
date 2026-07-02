using System.Reflection;
using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Policies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

public sealed class PluginAnalyzerKernelMethodEnumDefaultRegressionTests
{
    [Fact]
    public async Task KernelMethod_enum_default_argument_lowers_in_hook_chains()
    {
        var assembly = Compile("""
            using DotBoxD.Kernels;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public enum AggroBand : ulong
            {
                Low = 0,
                High = 0xFFFFFFFFFFFFFFFF
            }

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod.KernelMethodAggroEvent>()
                        .Where(e => IsHigh())
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "enum"));

                [KernelMethod]
                public static bool IsHigh(AggroBand band = AggroBand.High)
                    => band == AggroBand.High;
            }
            """);
        var package = HookChainPackage(assembly);
        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(messages, defaultPolicy: SandboxedPolicy());
        server.Hooks.On<KernelMethodAggroEvent>().UseGeneratedChain(package);

        await server.Hooks.PublishAsync(new KernelMethodAggroEvent("monster-1", 3, 10, 5));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("monster-1", message.TargetId);
        Assert.Equal("enum", message.Message);
    }

    [Fact]
    public async Task KernelMethod_guid_default_argument_lowers_in_hook_chains()
    {
        var assembly = Compile("""
            using System;
            using DotBoxD.Kernels;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod.KernelMethodAggroEvent>()
                        .Where(e => IsEmpty())
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "guid"));

                [KernelMethod]
                public static bool IsEmpty(Guid id = default)
                    => id == default;
            }
            """);
        var package = HookChainPackage(assembly);
        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(messages, defaultPolicy: SandboxedPolicy());
        server.Hooks.On<KernelMethodAggroEvent>().UseGeneratedChain(package);

        await server.Hooks.PublishAsync(new KernelMethodAggroEvent("monster-1", 3, 10, 5));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("monster-1", message.TargetId);
        Assert.Equal("guid", message.Message);
    }

    private static PluginPackage HookChainPackage(Assembly assembly)
    {
        var packageType = Assert.Single(assembly.GetTypes(), type =>
            type.Name.StartsWith("HookChain_", StringComparison.Ordinal) &&
            type.Name.EndsWith("PluginPackage", StringComparison.Ordinal));
        return (PluginPackage)packageType
            .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;
    }

    private static Assembly Compile(string source)
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);
        var compilation = CSharpCompilation.Create(
            "DotBoxDKernelMethodEnumDefaultTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Kernels.SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(KernelMethodAggroEvent).Assembly.Location)),
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

    private static SandboxPolicy SandboxedPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .WithMaxHostCalls(100)
            .Build();

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
