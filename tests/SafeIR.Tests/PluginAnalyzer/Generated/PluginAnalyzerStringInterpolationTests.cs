using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SafeIR.Hosting;
using SafeIR.PluginAnalyzer;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginAnalyzerStringInterpolationTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Generated_package_lowers_string_interpolation()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using SafeIR.Plugins;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string DamageType, string Message);

            [GamePlugin("generated-string-interpolation")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                private const string RequiredType = "fire";
                private const string RequiredSeverity = "critical";

                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => $"{e.DamageType}:{RequiredSeverity}" == $"{RequiredType}:critical";

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, $"hit:{e.Message}");
            }
            """);

        await AssertCompiledShouldHandleAsync(package, "fire", expected: true);
        await AssertCompiledShouldHandleAsync(package, "ice", expected: false);

        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        var kernel = await server.InstallAsync(package);

        await kernel.HandleAsync(new InterpolationEventAdapter(), new InterpolationEvent("player-1", "fire", "matched"));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-1", message.TargetId);
        Assert.Equal("hit:matched", message.Message);
    }

    [Fact]
    public void Generator_rejects_non_string_interpolation_holes()
    {
        var result = RunGenerator("""
            using SafeIR.Plugins;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, int Amount);

            [GamePlugin("bad-string-interpolation")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, $"amount:{e.Amount}");
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "SGP100");
        Assert.Empty(result.GeneratedTrees);
    }

    private static async Task AssertCompiledShouldHandleAsync(
        PluginPackage package,
        string damageType,
        bool expected)
    {
        var host = SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(new InMemoryPluginMessageSink());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var policy = SandboxPolicyBuilder.Create()
            .GrantGameMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .Build();
        var plan = await host.PrepareAsync(package.Module, policy);
        var result = await host.ExecuteAsync(
            plan,
            package.Entrypoints.ShouldHandle,
            Input(damageType),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, ExecutionFailure(result));
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(expected, ((BoolValue)result.Value!).Value);
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "SafeIrPluginStringInterpolationTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(GamePluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SafeIrPluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static SandboxValue Input(string damageType)
        => SandboxValue.FromList([
            SandboxValue.FromString("player-1"),
            SandboxValue.FromString(damageType),
            SandboxValue.FromString("matched")
        ]);

    private static string ExecutionFailure(SandboxExecutionResult result)
        => result.Error?.SafeMessage + Environment.NewLine +
           string.Join(Environment.NewLine, result.AuditEvents.Select(e => $"{e.Kind}: {e.ErrorCode} {e.Message}"));

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

    private sealed record InterpolationEvent(string TargetId, string DamageType, string Message);

    private sealed class InterpolationEventAdapter : IPluginEventAdapter<InterpolationEvent>
    {
        public string EventName => "DamageEvent";

        public IReadOnlyList<Parameter> Parameters { get; } = [
            new("e_TargetId", SandboxType.String),
            new("e_DamageType", SandboxType.String),
            new("e_Message", SandboxType.String)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(InterpolationEvent e)
            => [
                SandboxValue.FromString(e.TargetId),
                SandboxValue.FromString(e.DamageType),
                SandboxValue.FromString(e.Message)
            ];
    }
}
