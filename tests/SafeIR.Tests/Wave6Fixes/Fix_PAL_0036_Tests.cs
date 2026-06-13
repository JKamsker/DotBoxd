using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.PluginLocal;
using SafeIR.Plugins;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for PAL-0036: plugin kernel execution created a linked
/// <see cref="CancellationTokenSource"/> for every sandbox entrypoint invocation, even on
/// the common path where the caller supplies the default (non-cancelable) token and the only
/// live cancellation source is the kernel revocation token. <c>InstalledKernel.ExecutePreparedAsync</c>
/// unconditionally called <c>CancellationTokenSource.CreateLinkedTokenSource(callerToken, _revocation.Token)</c>,
/// allocating a linked source plus cancellation registrations before any Safe-IR code ran.
///
/// The fixed behavior passes the revocation token through directly when the caller token cannot be
/// canceled, allocating a linked source only when both tokens can independently fire. These tests
/// measure allocated bytes on the executing thread and assert that the default-token path allocates
/// strictly less than the both-cancelable path: the delta is exactly the linked <see cref="CancellationTokenSource"/>
/// that the common path must no longer pay. The assertion is red while linking is unconditional.
/// </summary>
public sealed class Fix_PAL_0036_Tests
{
    // Iterations to reach allocation steady state; large enough to average out JIT/first-call noise.
    private const int ExecutionIterations = 5_000;

    private sealed class DamageEventAdapter : IPluginEventAdapter<DamageEvent>
    {
        public string EventName => "DamageEvent";

        public IReadOnlyList<Parameter> Parameters { get; } = [
            new("e_DamageType", SandboxType.String),
            new("e_Amount", SandboxType.I32),
            new("e_TargetId", SandboxType.String)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageEvent e)
            => [
                SandboxValue.FromString(e.DamageType),
                SandboxValue.FromInt32(e.Amount),
                SandboxValue.FromString(e.TargetId)
            ];
    }

    [Fact]
    public async Task DefaultTokenExecution_doesNotAllocateLinkedCancellationSource()
    {
        var perCallDefault = await MeasureBytesPerShouldHandleAsync(useCancelableToken: false);
        var perCallCancelable = await MeasureBytesPerShouldHandleAsync(useCancelableToken: true);

        // A linked CancellationTokenSource plus its registration on a 64-bit runtime costs well
        // over this many bytes. The both-cancelable path must still allocate one (caller token and
        // revocation token can independently fire); the default-token path must not.
        const long linkedSourceFloorBytes = 64L;

        Assert.True(
            perCallCancelable - perCallDefault >= linkedSourceFloorBytes,
            $"Expected the default (non-cancelable) token path to skip the linked CancellationTokenSource: " +
            $"default={perCallDefault} bytes/call, cancelable={perCallCancelable} bytes/call " +
            $"(delta {perCallCancelable - perCallDefault} < {linkedSourceFloorBytes}). " +
            $"The linked source is still allocated unconditionally on the common path.");
    }

    private static async Task<long> MeasureBytesPerShouldHandleAsync(bool useCancelableToken)
    {
        var server = PluginAddendumTestPolicies.CreateServer(executionMode: ExecutionMode.Interpreted);
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        var adapter = new DamageEventAdapter();
        var e = new DamageEvent("fire", 120, "player-1");

        // When the caller passes a real, cancelable token the kernel must still link it with the
        // revocation token. The default token (CancellationToken.None) is non-cancelable.
        using var cts = useCancelableToken ? new CancellationTokenSource() : null;
        var token = cts?.Token ?? CancellationToken.None;

        // Warm up so first-call JIT and one-time allocations (validation cache, typed values) are
        // excluded from the measurement.
        for (var i = 0; i < 500; i++)
        {
            await kernel.ShouldHandleAsync(adapter, e, token);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < ExecutionIterations; i++)
        {
            await kernel.ShouldHandleAsync(adapter, e, token);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();
        return (after - before) / ExecutionIterations;
    }
}
