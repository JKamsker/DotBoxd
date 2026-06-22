namespace DotBoxD.Plugins.Kernel;

public sealed class InstalledKernelPool
{
    private readonly InstalledKernel[] _kernels;
    private int _next = -1;

    internal InstalledKernelPool(IReadOnlyList<InstalledKernel> kernels)
    {
        ArgumentNullException.ThrowIfNull(kernels);
        if (kernels.Count == 0)
        {
            throw new ArgumentException("kernel pool must contain at least one kernel", nameof(kernels));
        }

        _kernels = new InstalledKernel[kernels.Count];
        for (var i = 0; i < kernels.Count; i++)
        {
            _kernels[i] = kernels[i] ?? throw new ArgumentException("kernel pool cannot contain null kernels", nameof(kernels));
        }

        PluginId = _kernels[0].Manifest.PluginId;
        Kernels = Array.AsReadOnly(_kernels);
    }

    public string PluginId { get; }
    public int DegreeOfParallelism => _kernels.Length;
    public IReadOnlyList<InstalledKernel> Kernels { get; }

    public bool IsRevoked
    {
        get
        {
            for (var i = 0; i < _kernels.Length; i++)
            {
                if (!_kernels[i].IsRevoked)
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal void Revoke()
    {
        for (var i = 0; i < _kernels.Length; i++)
        {
            _kernels[i].Revoke();
        }
    }

    internal void ValidateFor<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        for (var i = 0; i < _kernels.Length; i++)
        {
            _kernels[i].ValidateFor(adapter);
        }
    }

    internal ValueTask InvokeAsync<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        CancellationToken cancellationToken)
        => SelectForDispatch().InvokeAsync(adapter, e, cancellationToken);

    private InstalledKernel SelectForDispatch()
    {
        var next = unchecked((uint)Interlocked.Increment(ref _next));
        var index = (int)(next % (uint)_kernels.Length);
        return _kernels[index];
    }
}
