namespace DotBoxD.Hosting;

using DotBoxD.Kernels;

internal sealed class CompiledNoAuditRunState(ExecutionPlan plan)
{
    private string? _cachedEntrypoint;
    private CompiledExecutable _cachedExecutable;
    private Dictionary<string, CompiledExecutable>? _executables;
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

    public bool TryGetExecutable(string entrypoint, out CompiledExecutable executable)
    {
        if (StringComparer.Ordinal.Equals(_cachedEntrypoint, entrypoint))
        {
            executable = _cachedExecutable;
            return true;
        }

        if (_executables is not null && _executables.TryGetValue(entrypoint, out executable))
        {
            _cachedEntrypoint = entrypoint;
            _cachedExecutable = executable;
            return true;
        }

        executable = default;
        return false;
    }

    public void StoreExecutable(string entrypoint, CompiledExecutable executable)
    {
        _cachedEntrypoint = entrypoint;
        _cachedExecutable = executable;
        (_executables ??= new Dictionary<string, CompiledExecutable>(StringComparer.Ordinal))[entrypoint] = executable;
    }
}
