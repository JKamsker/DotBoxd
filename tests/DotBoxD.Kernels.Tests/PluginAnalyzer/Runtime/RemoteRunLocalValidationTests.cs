using System.Reflection;
using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

/// <summary>
/// Convergence phase 3: the runtime validators must accept the shapes the lifted analyzer caps now produce —
/// a whole-event RunLocal (no Select, Unit Handle) and a non-scalar projection RunLocal (non-Unit Handle of a
/// type outside the old 5-scalar set) — while still requiring Unit for ordinary chains. Installing a package
/// runs <c>PluginPreparedPackageValidator</c>; these tests assert install succeeds (no validation throw) and
/// the manifest mark is correct.
/// </summary>
public sealed class RemoteRunLocalValidationTests
{
    private const string WholeEventSource = """
        using DotBoxD.Plugins.Runtime;
        namespace ChainSample;
        public static class WholeEventUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 4)
                    .RunLocal((e, ctx) => { });
        }
        """;

    private const string ScalarProjectionSource = """
        using DotBoxD.Plugins.Runtime;
        namespace ChainSample;
        public static class ScalarProjectionUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 4)
                    .Select(e => e.MonsterId)
                    .RunLocal((id, ctx) => { });
        }
        """;

    private const string ListProjectionSource = """
        using System.Collections.Generic;
        using DotBoxD.Plugins.Runtime;
        namespace ChainSample;
        public sealed record InventoryEvent(string OwnerId, List<int> Quantities);
        public static class ListProjectionUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<InventoryEvent>()
                    .Where(e => e.OwnerId == "p1")
                    .Select(e => e.Quantities)
                    .RunLocal((quantities, ctx) => { });
        }
        """;

    [Fact]
    public async Task Whole_event_RunLocal_no_select_lowers_to_a_unit_handle_and_installs()
    {
        var package = LowerToPackage(WholeEventSource);

        var subscription = Assert.Single(package.Manifest.Subscriptions);
        Assert.True(subscription.LocalTerminal);        // marked local-terminal
        Assert.Null(subscription.ProjectedType);        // whole-event => no projected type

        using var server = PluginServer.Create(new InMemoryPluginMessageSink(), defaultPolicy: Policy());
        // Installing runs PluginPreparedPackageValidator over the Unit-returning Handle: must NOT throw now
        // that whole-event (no Select) is a valid local-terminal shape.
        var kernel = await server.InstallAsync(package);
        Assert.True(kernel.Manifest.Subscriptions[0].LocalTerminal);
    }

    [Fact]
    public async Task Scalar_projection_RunLocal_installs_with_projected_type()
    {
        var package = LowerToPackage(ScalarProjectionSource);

        var subscription = Assert.Single(package.Manifest.Subscriptions);
        Assert.True(subscription.LocalTerminal);
        Assert.Equal("string", subscription.ProjectedType);

        using var server = PluginServer.Create(new InMemoryPluginMessageSink(), defaultPolicy: Policy());
        await server.InstallAsync(package);   // non-Unit Handle accepted for a projection chain
    }

    // Non-scalar PROJECTION (e.g. a List-typed event property) is NOT lowered by the analyzer on EITHER source
    // branch — the expression/event-property lowering yields scalars only, so a list projection is unsupported.
    // It must FAIL SAFE: no verified-IR package is produced, so the un-intercepted RunLocal call site remains a
    // throwing stub rather than emitting an unmarshallable package. (A non-scalar RECORD reaches the wire via
    // the whole-event path; non-scalar projection lowering is a documented follow-up — the lifted validator cap
    // is already ready for it.)
    [Fact]
    public void Non_scalar_list_projection_does_not_lower_and_RunLocal_stays_a_throwing_stub()
    {
        var assembly = Compile(ListProjectionSource);

        // No verified-IR package is generated for a non-scalar projection — fail safe, not a broken package.
        Assert.DoesNotContain(assembly.GetTypes(), type =>
            type.Name.StartsWith("HookChain_", StringComparison.Ordinal) &&
            type.Name.EndsWith("PluginPackage", StringComparison.Ordinal));

        // The .RunLocal call site was not intercepted (the chain did not lower), so invoking Configure hits the
        // runtime RunLocal stub, which throws — the un-lowered RunLocal never runs unsandboxed.
        var usage = assembly.GetType("ChainSample.ListProjectionUsage")!;
        var registry = new RemoteHookRegistry(_ => ValueTask.FromResult("unused"), new RemoteLocalHandlerRegistry());
        var ex = Assert.Throws<TargetInvocationException>(() =>
            usage.GetMethod("Configure", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [registry]));
        Assert.IsType<NotSupportedException>(ex.InnerException);
    }

    private static SandboxPolicy Policy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private static PluginPackage LowerToPackage(string source)
    {
        var assembly = Compile(source);
        var packageType = assembly.GetTypes().Single(type =>
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
            "DotBoxDRemoteRunLocalValidationTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(ChainAggroEvent).Assembly.Location)),
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

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
