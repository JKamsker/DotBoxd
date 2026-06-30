using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Bindings;

internal interface IOneArgumentBindingInvoker
{
    ValueTask<SandboxValue> Invoke(
        SandboxContext context,
        SandboxValue arg0,
        CancellationToken cancellationToken);
}

internal interface ITwoArgumentBindingInvoker
{
    ValueTask<SandboxValue> Invoke(
        SandboxContext context,
        SandboxValue arg0,
        SandboxValue arg1,
        CancellationToken cancellationToken);
}
