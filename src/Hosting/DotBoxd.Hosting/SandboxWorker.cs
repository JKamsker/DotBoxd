namespace DotBoxd.Hosting;

using DotBoxd.Kernels;

/// <summary>
/// Advanced SPI for wiring <see cref="SandboxIsolation.WorkerProcess"/> to a real out-of-process
/// boundary. A consumer implements the transport/process lifecycle and runs the requested plan on
/// the worker side. The host re-validates the returned result envelope before publishing it, so a
/// misbehaving worker fails closed rather than escaping the sandbox contract.
/// </summary>
/// <remarks>
/// Most hosts should not implement this interface by hand. Use the shipped
/// <see cref="SandboxHostWorkerClient"/> reference adapter on the worker side of the boundary, which
/// re-prepares the plan from its module and policy and produces a validator-compliant envelope.
/// </remarks>
public interface ISandboxWorkerClient
{
    ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reference <see cref="ISandboxWorkerClient"/> that bridges a worker request to a worker-side
/// <see cref="SandboxHost"/>. The worker independently re-prepares the plan from the transmitted
/// module and policy instead of trusting the requesting host's signed plan, then executes it with
/// the supplied (in-process) options. When the worker is hosted in a separate process, container,
/// or restricted account, this provides a real out-of-process boundary with a documented envelope.
/// </summary>
/// <remarks>
/// The supplied factory is invoked once per request so the worker-side host can be scoped to the
/// boundary's lifetime. The worker host's bindings must match the requesting host's bindings;
/// otherwise the re-prepared identity hashes diverge and the requesting host fails the result
/// closed. Re-preparation or execution failures are surfaced as a closed, fail-safe error result.
/// </remarks>
public sealed class SandboxHostWorkerClient : ISandboxWorkerClient
{
    private readonly Func<SandboxHost> _hostFactory;

    public SandboxHostWorkerClient(Func<SandboxHost> hostFactory)
        => _hostFactory = hostFactory ?? throw new ArgumentNullException(nameof(hostFactory));

    public async ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);

        var workerHost = _hostFactory()
            ?? throw new InvalidOperationException("worker host factory returned null");
        using (workerHost)
        {
            var workerPlan = await workerHost
                .PrepareAsync(plan.Module, plan.Policy, cancellationToken)
                .ConfigureAwait(false);
            return await workerHost
                .ExecuteAsync(workerPlan, entrypoint, input, options, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

public sealed record SandboxWorkerProfile(
    bool OutOfProcess,
    bool SecretsIsolated,
    bool ResourceLimitsConfigured)
{
    public static SandboxWorkerProfile HardenedOutOfProcess { get; } = new(
        OutOfProcess: true,
        SecretsIsolated: true,
        ResourceLimitsConfigured: true);

    internal bool SatisfiesWorkerProcess
        => OutOfProcess && SecretsIsolated && ResourceLimitsConfigured;

    internal IReadOnlyDictionary<string, string> ToAuditFields()
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["outOfProcess"] = OutOfProcess.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["secretsIsolated"] = SecretsIsolated.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["resourceLimitsConfigured"] = ResourceLimitsConfigured.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
}

internal sealed record ConfiguredSandboxWorker(
    ISandboxWorkerClient Client,
    SandboxWorkerProfile Profile);
