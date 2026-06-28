namespace DotBoxD.Plugins.Runtime.Hooks;

using System.Runtime.CompilerServices;

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

        if (typeof(TProjected).IsEnum &&
            Enum.GetUnderlyingType(typeof(TProjected)) == typeof(int))
        {
            invoke = (value, context) => handler(FromInt32Enum<TProjected>(value.Int32Value), context);
            return true;
        }

        invoke = null!;
        return false;
    }

    private static TEnum FromInt32Enum<TEnum>(int value)
        => Unsafe.As<int, TEnum>(ref value);
}
