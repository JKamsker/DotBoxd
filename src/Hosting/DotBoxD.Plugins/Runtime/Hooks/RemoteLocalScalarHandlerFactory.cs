namespace DotBoxD.Plugins.Runtime.Hooks;

internal static class RemoteLocalScalarHandlerFactory
{
    public static bool TryCreate<TProjected>(
        Func<TProjected, HookContext, ValueTask> handler,
        out Func<KernelRpcValue, HookContext, ValueTask> invoke)
    {
        if (typeof(TProjected) == typeof(int))
        {
            var typed = (Func<int, HookContext, ValueTask>)(object)handler;
            invoke = (value, context) => typed(value.Int32Value, context);
            return true;
        }

        invoke = null!;
        return false;
    }
}
