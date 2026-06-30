using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime;

public static partial class CompiledRuntime
{
    public static SandboxValue CallBinding(SandboxContext context, string id, SandboxValue[] args)
        => CompiledBindingDispatcher.CallBinding(context, id, args);

    public static SandboxValue CallBinding1(SandboxContext context, string id, SandboxValue arg0)
        => CompiledBindingDispatcher.CallBinding1(context, id, arg0);

    public static SandboxValue CallBinding2(SandboxContext context, string id, SandboxValue arg0, SandboxValue arg1)
        => CompiledBindingDispatcher.CallBinding2(context, id, arg0, arg1);
}
