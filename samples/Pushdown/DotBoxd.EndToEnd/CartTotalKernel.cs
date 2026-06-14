namespace DotBoxd.EndToEnd;

using DotBoxd.Hosting;
using DotBoxd.Kernels;
using DotBoxd.Kernels.Serialization.Json;

/// <summary>
/// Hosts a validated DotBoxd kernel that sums a list of per-line subtotals. This is the "logic" that
/// runs <i>server-side</i>, next to the host catalog service, instead of being composed by the client.
///
/// The kernel is plain JSON IR: it takes a <c>List&lt;I32&gt;</c> parameter, walks it with a bounded
/// <c>forRange</c> loop, and accumulates the sum with the sandbox <c>add</c> operator. It is validated
/// and executed under a <see cref="SandboxPolicy"/> with a fuel + loop-iteration budget, so a malicious
/// or buggy kernel cannot run away with host resources.
/// </summary>
public sealed class CartTotalKernel : IDisposable
{
    // List<I32> -> I32. forRange over [0, count) reads each subtotal via list.get and adds it to acc.
    // Uses only the default pure bindings (list.count, list.get) plus the built-in "add" operator.
    private const string KernelJson = """
    {
      "id": "cart-total",
      "version": "1.0.0",
      "functions": [
        {
          "id": "main",
          "visibility": "entrypoint",
          "parameters": [
            { "name": "subtotals", "type": { "name": "List", "arguments": ["I32"] } }
          ],
          "returnType": "I32",
          "body": [
            { "op": "set", "name": "acc", "value": { "i32": 0 } },
            {
              "op": "forRange",
              "local": "i",
              "start": { "i32": 0 },
              "end": { "call": "list.count", "args": [{ "var": "subtotals" }] },
              "body": [
                {
                  "op": "set",
                  "name": "acc",
                  "value": {
                    "op": "add",
                    "left": { "var": "acc" },
                    "right": { "call": "list.get", "args": [{ "var": "subtotals" }, { "var": "i" }] }
                  }
                }
              ]
            },
            { "op": "return", "value": { "var": "acc" } }
          ]
        }
      ]
    }
    """;

    private readonly SandboxHost _host;
    private ExecutionPlan? _plan;

    private CartTotalKernel(SandboxHost host) => _host = host;

    /// <summary>Builds the host with the default pure bindings and the interpreter execution engine.</summary>
    public static CartTotalKernel Create()
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });

        return new CartTotalKernel(host);
    }

    /// <summary>
    /// Validates the kernel IR against the policy (once) and runs it over <paramref name="subtotals"/>.
    /// Returns the kernel's computed total and the fuel it metered. Throws if the sandbox rejects the run
    /// (for example on a quota or validation failure) so the host never returns a fabricated total.
    /// </summary>
    public async ValueTask<(int Total, long FuelUsed)> RunAsync(
        IReadOnlyList<int> subtotals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subtotals);

        // Bounded budget: the kernel is sandboxed logic, so it runs under explicit fuel and loop limits.
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(1_000_000)
            .WithMaxLoopIterations(10_000)
            .WithMaxListLength(10_000)
            .Build();

        var module = await _host.ImportJsonAsync(KernelJson, cancellationToken).ConfigureAwait(false);
        _plan ??= await _host.PrepareAsync(module, policy, cancellationToken).ConfigureAwait(false);

        var input = SandboxValue.FromList(
            [.. subtotals.Select(SandboxValue.FromInt32)],
            SandboxType.I32);

        var result = await _host.ExecuteAsync(_plan, "main", input, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded || result.Value is not I32Value computed)
        {
            throw new InvalidOperationException(
                $"cart-total kernel did not produce an I32 result (error: {result.Error?.Code.ToString() ?? "none"}).");
        }

        return (computed.Value, result.ResourceUsage.FuelUsed);
    }

    public void Dispose() => _host.Dispose();
}
