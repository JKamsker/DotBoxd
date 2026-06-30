using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Policies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

public sealed class PluginAnalyzerKernelMethodTemporalDefaultRegressionTests
{
    [Fact]
    public async Task KernelMethod_datetime_default_argument_lowers_in_hook_chains()
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
                        .Where(e => Matches())
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "datetime"));

                [KernelMethod]
                public static bool Matches(DateTime startedAt = default)
                    => true;
            }
            """);
        var package = HookChainPackage(assembly);
        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(messages, defaultPolicy: SandboxedPolicy());
        server.Hooks.On<KernelMethodAggroEvent>().UseGeneratedChain(package);

        await server.Hooks.PublishAsync(new KernelMethodAggroEvent("monster-1", 3, 10, 5));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("monster-1", message.TargetId);
        Assert.Equal("datetime", message.Message);
    }

    [Fact]
    public void KernelMethod_metadata_style_datetime_default_argument_lowers_in_hook_chains()
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator("""
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            using DotBoxD.Kernels;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod.KernelMethodAggroEvent>()
                        .Where(e => Matches())
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "datetime-metadata"));

                [KernelMethod]
                public static bool Matches([Optional, DateTimeConstant(0L)] DateTime startedAt)
                    => startedAt == default;
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK114");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("UseGeneratedChain", StringComparison.Ordinal));
    }

    [Fact]
    public void KernelMethod_fake_datetime_constant_attribute_stays_unsupported()
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator("""
            using System;
            using System.Runtime.InteropServices;
            using DotBoxD.Kernels;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class DateTimeConstantAttribute(long ticks) : Attribute
            {
                public long Ticks { get; } = ticks;
            }

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod.KernelMethodAggroEvent>()
                        .Where(e => Matches())
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "fake-datetime-metadata"));

                [KernelMethod]
                public static bool Matches([Optional, DateTimeConstant(0L)] DateTime startedAt)
                    => startedAt == default;
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "DBXK114");
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("UseGeneratedChain", StringComparison.Ordinal));
    }

    [Fact]
    public void KernelMethod_out_of_range_datetime_constant_stays_unsupported()
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGeneratorWithReferences("""
            using DotBoxD.Kernels;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod.KernelMethodAggroEvent>()
                        .Where(e => global::Sample.Metadata.BadDefaults.Matches())
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "invalid-datetime-metadata"));
            }
            """, InvalidDateTimeConstantKernelMethodReference());

        Assert.Contains(result.Diagnostics, d => d.Id == "DBXK114");
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("UseGeneratedChain", StringComparison.Ordinal));
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
            "DotBoxDKernelMethodTemporalDefaultTest",
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

    private static MetadataReference InvalidDateTimeConstantKernelMethodReference()
    {
        using var module = ModuleDefinition.CreateModule("InvalidDateTimeConstantKernelMethods", ModuleKind.Dll);
        var type = new TypeDefinition(
            "Sample.Metadata",
            "BadDefaults",
            Mono.Cecil.TypeAttributes.Public |
            Mono.Cecil.TypeAttributes.Abstract |
            Mono.Cecil.TypeAttributes.Sealed |
            Mono.Cecil.TypeAttributes.Class |
            Mono.Cecil.TypeAttributes.BeforeFieldInit,
            module.ImportReference(typeof(object)));
        module.Types.Add(type);

        var method = new MethodDefinition(
            "Matches",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
            module.TypeSystem.Boolean);
        method.CustomAttributes.Add(CustomAttribute(
            module,
            typeof(DotBoxD.Abstractions.KernelMethodAttribute).GetConstructor(Type.EmptyTypes)!));
        method.Parameters.Add(InvalidDateTimeParameter(module));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);

        using var stream = new MemoryStream();
        module.Write(stream);
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static ParameterDefinition InvalidDateTimeParameter(ModuleDefinition module)
    {
        var parameter = new ParameterDefinition(
            "startedAt",
            Mono.Cecil.ParameterAttributes.Optional,
            module.ImportReference(typeof(DateTime)));
        parameter.CustomAttributes.Add(CustomAttribute(module, typeof(OptionalAttribute).GetConstructor(Type.EmptyTypes)!));

        var dateTimeConstant = CustomAttribute(
            module,
            typeof(DateTimeConstantAttribute).GetConstructor([typeof(long)])!);
        dateTimeConstant.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.Int64, -1L));
        parameter.CustomAttributes.Add(dateTimeConstant);
        return parameter;
    }

    private static CustomAttribute CustomAttribute(ModuleDefinition module, ConstructorInfo constructor)
        => new(module.ImportReference(constructor));

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
