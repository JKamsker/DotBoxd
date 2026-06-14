namespace DotBoxD.Hosting;

using DotBoxD.Kernels;

internal sealed class CompiledNoAuditRunState(ExecutionPlan plan)
{
    private SandboxContext? _context;
    private CancellationToken _contextToken;

    public ResourceMeter Budget { get; } = new(plan.Budget);

    public SandboxContext ContextFor(
        IReadOnlySet<string> allowedBindings,
        CancellationToken cancellationToken)
    {
        Budget.ResetForReuse();
        if (_context is not null && _contextToken.Equals(cancellationToken))
        {
            _context.ResetForCompiledNoAuditReuse();
            return _context;
        }

        _contextToken = cancellationToken;
        _context = new SandboxContext(
            SandboxRunId.Suppressed,
            plan.Policy,
            Budget,
            plan.Bindings,
            NoopAuditSink.Instance,
            cancellationToken,
            allowedBindings,
            plan.ModuleHash,
            plan.PolicyHash);
        return _context;
    }
}
