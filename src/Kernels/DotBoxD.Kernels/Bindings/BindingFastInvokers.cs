namespace DotBoxD.Kernels;

internal interface ITwoArgumentBindingInvoker
{
    ValueTask<SandboxValue> Invoke(
        SandboxContext context,
        SandboxValue arg0,
        SandboxValue arg1,
        CancellationToken cancellationToken);
}
