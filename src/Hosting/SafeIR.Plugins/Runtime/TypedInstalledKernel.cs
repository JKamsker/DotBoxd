namespace SafeIR.Plugins;

public sealed class TypedInstalledKernel<TSettings> where TSettings : class
{
    internal TypedInstalledKernel(InstalledKernel kernel)
    {
        Kernel = kernel;
        Value = kernel.GetTypedValue<TSettings>();
    }

    public InstalledKernel Kernel { get; }
    public TSettings Value { get; }

    public LiveUpdateMode UpdateMode
    {
        get => Kernel.GetUpdateMode(typeof(TSettings));
        set => Kernel.SetUpdateMode(typeof(TSettings), value);
    }

    public Exception? LastAsyncUpdateError => Kernel.LastAsyncUpdateError;

    public ValueTask FlushUpdatesAsync(CancellationToken cancellationToken = default)
        => Kernel.FlushUpdatesAsync(cancellationToken);

    public ValueTask ModifyAsync(
        Action<TSettings> modify,
        bool atomic = false,
        CancellationToken cancellationToken = default)
        => Kernel.ModifyAsync(Value, modify, atomic, cancellationToken);
}
