namespace DotBoxd.Kernels.Benchmarks.Core;

using BenchmarkDotNet.Attributes;
using DotBoxd.Hosting;

[MemoryDiagnoser]
public class BindingReferencePlanBenchmarks
{
    private static readonly SourceSpan Span = new(0, 0);

    private SandboxHost _host = null!;
    private SandboxModule _module = null!;
    private SandboxPolicy _policy = null!;

    [Params(1, 10, 100)]
    public int EntrypointCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
        });
        _module = BuildModule(EntrypointCount);
        _policy = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithFuel(100_000)
            .Build();
    }

    [Benchmark]
    public async ValueTask<ExecutionPlan> PrepareSharedHelperGraph()
        => await _host.PrepareAsync(_module, _policy);

    private static SandboxModule BuildModule(int entrypointCount)
    {
        var functions = new List<SandboxFunction>(entrypointCount + 3);
        for (var i = 0; i < entrypointCount; i++) {
            functions.Add(new SandboxFunction(
                $"main{i}",
                true,
                [],
                SandboxType.Unit,
                [new ReturnStatement(new CallExpression("helper0", [], null, Span), Span)]));
        }

        functions.Add(new SandboxFunction(
            "helper0",
            false,
            [],
            SandboxType.Unit,
            [new ReturnStatement(new CallExpression("helper1", [], null, Span), Span)]));
        functions.Add(new SandboxFunction(
            "helper1",
            false,
            [],
            SandboxType.Unit,
            [new ReturnStatement(new CallExpression("helper2", [], null, Span), Span)]));
        functions.Add(new SandboxFunction(
            "helper2",
            false,
            [],
            SandboxType.Unit,
            [
                new ReturnStatement(
                    new CallExpression(
                        "log.info",
                        [new LiteralExpression(SandboxValue.FromString("shared"), Span)],
                        null,
                        Span),
                    Span)
            ]));

        return new SandboxModule(
            "binding-reference-plan-benchmark",
            new SemVersion(1, 0, 0),
            new SemVersion(1, 0, 0),
            [],
            functions,
            new Dictionary<string, string>());
    }
}
