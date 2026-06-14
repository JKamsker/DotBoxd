namespace DotBoxd.Kernels.Benchmarks.Http;

using System.Net;
using BenchmarkDotNet.Attributes;
using DotBoxd.Kernels.Runtime;
using DotBoxd.Hosting.Http;

[MemoryDiagnoser]
public class HttpGrantParsingBenchmarks
{
    private BindingRegistry _bindings = null!;
    private SandboxContext _context = null!;
    private SandboxPolicy _policy = null!;
    private SafeInMemoryHttpMessageInvoker _invoker = null!;
    private readonly SandboxUri _uri = new("https://api.example.com/config");

    [Params(0, 32, 1024, 65536)]
    public int ResponseBytes { get; set; }

    [Params(1, 10, 1_000)]
    public int RequestCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _invoker = new SafeInMemoryHttpMessageInvoker(new byte[ResponseBytes]);
        _bindings = new BindingRegistryBuilder()
            .AddNetworkBindings(_invoker, StaticDns)
            .Build();
        _policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1_000_000)
            .WithFuel(10_000_000)
            .WithMaxAllocatedBytes(10_000_000)
            .Build();
    }

    [IterationSetup]
    public void ResetContext()
        => _context = new SandboxContext(
            SandboxRunId.New(),
            _policy,
            new ResourceMeter(_policy.ResourceLimits),
            _bindings,
            new InMemoryAuditSink(),
            CancellationToken.None);

    [Benchmark]
    public async ValueTask RepeatedHttpGets()
    {
        for (var i = 0; i < RequestCount; i++)
        {
            _ = await SafeHttpClient.GetTextAsync(_context, _uri, _invoker, StaticDns, CancellationToken.None);
        }
    }

    private static ValueTask<IReadOnlyList<IPAddress>> StaticDns(string host, CancellationToken cancellationToken)
        => ValueTask.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("203.0.113.10")]);
}
