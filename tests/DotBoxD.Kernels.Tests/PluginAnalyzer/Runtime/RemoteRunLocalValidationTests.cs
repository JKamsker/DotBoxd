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
/// a whole-event RunLocal (no Select, explicit event-record Handle) and a non-scalar projection RunLocal
/// (non-Unit Handle of a type outside the old 5-scalar set) — while still requiring Unit for ordinary chains.
/// Installing a package runs <c>PluginPreparedPackageValidator</c>; these tests assert install succeeds
/// (no validation throw) and the manifest mark is correct.
/// </summary>
public sealed partial class RemoteRunLocalValidationTests
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

    private const string OrdinaryRunSource = """
        using DotBoxD.Plugins.Runtime;
        namespace ChainSample;
        public static class OrdinaryRunUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 4)
                    .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
        }
        """;

    // An event carrying a genuinely unmarshallable property (object has no wire representation) must STILL fail
    // safe: no verified-IR package is produced and the un-intercepted RunLocal call site stays a throwing stub.
    private const string UnmarshallableEventSource = """
        using DotBoxD.Plugins.Runtime;
        namespace ChainSample;
        public sealed record UnmarshallableEvent(string Id, object Payload);
        public static class UnmarshallableUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<UnmarshallableEvent>()
                    .Where(e => e.Id == "x")
                    .RunLocal((e, ctx) => { });
        }
        """;

    [Fact]
    public async Task Whole_event_RunLocal_no_select_lowers_to_an_event_projection_and_installs()
    {
        var package = LowerToPackage(WholeEventSource);

        var subscription = Assert.Single(package.Manifest.Subscriptions);
        Assert.True(subscription.LocalTerminal);        // marked local-terminal
        Assert.Equal("global::" + typeof(ChainAggroEvent).FullName, subscription.ProjectedType);

        using var server = PluginServer.Create(new InMemoryPluginMessageSink(), defaultPolicy: Policy());
        // Installing runs PluginPreparedPackageValidator over the value-returning Handle: a no-Select RunLocal
        // stays author-friendly while still being distinguishable from an ordinary Unit-returning Run.
        var kernel = await server.InstallAsync(WithCallbackSubscriptionId(package));
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
        await server.InstallAsync(WithCallbackSubscriptionId(package));   // non-Unit Handle accepted for a projection chain
    }

    // A non-scalar PROJECTION (here a List-typed event property) now lowers: the Where/Select run server-side and
    // the projected List is pushed over the wire. This is the documented follow-up the lifted validator cap was
    // already ready for — it installs with a non-null projected-type mark.
    [Fact]
    public async Task List_projection_RunLocal_lowers_and_installs()
    {
        var package = LowerToPackage(ListProjectionSource);

        var subscription = Assert.Single(package.Manifest.Subscriptions);
        Assert.True(subscription.LocalTerminal);
        Assert.Equal("list", subscription.ProjectedType);   // non-null => projection push

        using var server = PluginServer.Create(new InMemoryPluginMessageSink(), defaultPolicy: Policy());
        await server.InstallAsync(WithCallbackSubscriptionId(package));
    }

    [Fact]
    public async Task Ordinary_Run_package_tampered_to_current_whole_event_RunLocal_is_rejected_at_install()
    {
        var package = LowerToPackage(OrdinaryRunSource);
        var subscription = Assert.Single(package.Manifest.Subscriptions);
        var metadata = new Dictionary<string, string>(package.Module.Metadata, StringComparer.Ordinal)
        {
            ["callbackSubscriptionId"] = "callback-tamper"
        };
        var tampered = package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions = [subscription with { LocalTerminal = true, ProjectedType = "record" }]
            },
            Module = package.Module with { Metadata = metadata }
        };

        using var server = PluginServer.Create(new InMemoryPluginMessageSink(), defaultPolicy: Policy());
        var ex = await Assert.ThrowsAsync<DotBoxD.Kernels.Model.SandboxValidationException>(
            async () => await server.InstallAsync(tampered).AsTask());
        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK031");
    }

    [Fact]
    public async Task Ordinary_Run_package_tampered_to_legacy_null_projection_RunLocal_is_rejected_at_install()
    {
        var package = LowerToPackage(OrdinaryRunSource);
        var subscription = Assert.Single(package.Manifest.Subscriptions);
        var metadata = new Dictionary<string, string>(package.Module.Metadata, StringComparer.Ordinal)
        {
            ["callbackSubscriptionId"] = "callback-tamper"
        };
        var tampered = package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions = [subscription with { LocalTerminal = true }]
            },
            Module = package.Module with { Metadata = metadata }
        };

        using var server = PluginServer.Create(new InMemoryPluginMessageSink(), defaultPolicy: Policy());
        var ex = await Assert.ThrowsAsync<DotBoxD.Kernels.Model.SandboxValidationException>(
            async () => await server.InstallAsync(tampered).AsTask());
        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK031");
    }

    [Fact]
    public async Task Whole_event_RunLocal_package_tampered_to_legacy_null_projection_is_rejected_at_install()
    {
        var package = LowerToPackage(WholeEventSource);
        var subscription = Assert.Single(package.Manifest.Subscriptions);
        var tampered = package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions = [subscription with { ProjectedType = null }]
            }
        };

        using var server = PluginServer.Create(new InMemoryPluginMessageSink(), defaultPolicy: Policy());
        var ex = await Assert.ThrowsAsync<DotBoxD.Kernels.Model.SandboxValidationException>(
            async () => await server.InstallAsync(tampered).AsTask());
        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK031");
    }

    // A genuinely unmarshallable property (object has no sandbox/wire representation) must STILL fail safe: no
    // verified-IR package is produced, so the un-intercepted RunLocal call site remains a throwing stub rather
    // than emitting a package whose value the host cannot build.
    [Fact]
    public void Unmarshallable_event_property_does_not_lower_and_RunLocal_stays_a_throwing_stub()
    {
        var assembly = Compile(UnmarshallableEventSource);

        Assert.DoesNotContain(assembly.GetTypes(), type =>
            type.Name.StartsWith("HookChain_", StringComparison.Ordinal) &&
            type.Name.EndsWith("PluginPackage", StringComparison.Ordinal));

        // The .RunLocal call site was not intercepted (the chain did not lower), so invoking Configure hits the
        // runtime RunLocal stub, which throws — the un-lowered RunLocal never runs unsandboxed.
        var usage = assembly.GetType("ChainSample.UnmarshallableUsage")!;
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

    private static PluginPackage WithCallbackSubscriptionId(PluginPackage package)
        => LocalTerminalIdentity.WithCallbackSubscriptionId(package, LocalTerminalIdentity.CreateCallbackSubscriptionId());

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
