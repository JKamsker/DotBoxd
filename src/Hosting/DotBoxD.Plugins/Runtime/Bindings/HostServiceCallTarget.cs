using System.Collections.Concurrent;
using System.Reflection;
using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Hosting.Execution;

internal sealed class HostServiceCallTarget
{
    private static readonly ConcurrentDictionary<Type, Func<object?, ValueTask<object?>>> ReturnReaders = new();
    private static readonly MethodInfo ReadGenericTaskMethod =
        typeof(HostServiceCallTarget).GetMethod(nameof(ReadGenericTaskAsync), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo ReadGenericValueTaskMethod =
        typeof(HostServiceCallTarget).GetMethod(nameof(ReadGenericValueTaskAsync), BindingFlags.Static | BindingFlags.NonPublic)!;

    private readonly Func<object?, object?[], object?> _invoke;
    private readonly Func<object?, ValueTask<object?>> _readReturn;

    public HostServiceCallTarget(MethodInfo method)
    {
        ReturnType = method.ReturnType;
        ParameterTypes = method.GetParameters()
            .Select(static parameter => parameter.ParameterType)
            .ToArray();
        _invoke = CreateInvoker(method);
        _readReturn = ReturnReaders.GetOrAdd(method.ReturnType, CreateReturnReader);
    }

    public Type ReturnType { get; }

    public Type[] ParameterTypes { get; }

    public object? Invoke(object? target, object?[] arguments)
        => _invoke(target, arguments);

    public ValueTask<object?> ReadReturnAsync(object? result)
        => _readReturn(result);

    public static bool IsTaskLike(Type type)
        => type == typeof(Task) ||
           type == typeof(ValueTask) ||
           IsGenericTask(type) ||
           IsGenericValueTask(type);

    public static Type? UnwrapReturnType(Type type)
    {
        if (type == typeof(void) || type == typeof(Task) || type == typeof(ValueTask))
        {
            return null;
        }

        if ((IsGenericTask(type) || IsGenericValueTask(type)) && type.GetGenericArguments() is [var payload])
        {
            return payload;
        }

        return type;
    }

    private static Func<object?, object?[], object?> CreateInvoker(MethodInfo method)
    {
        try
        {
            var target = LinqExpression.Parameter(typeof(object), "target");
            var arguments = LinqExpression.Parameter(typeof(object?[]), "arguments");
            var parameters = method.GetParameters();
            var callArguments = new LinqExpression[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var value = LinqExpression.ArrayIndex(arguments, LinqExpression.Constant(i));
                callArguments[i] = LinqExpression.Convert(value, parameters[i].ParameterType);
            }

            var instance = method.IsStatic
                ? null
                : LinqExpression.Convert(target, method.DeclaringType!);
            var call = LinqExpression.Call(instance, method, callArguments);
            LinqExpression body = method.ReturnType == typeof(void)
                ? LinqExpression.Block(call, LinqExpression.Constant(null, typeof(object)))
                : LinqExpression.Convert(call, typeof(object));
            return LinqExpression.Lambda<Func<object?, object?[], object?>>(body, target, arguments).Compile();
        }
        catch (ArgumentException)
        {
            return method.Invoke;
        }
        catch (InvalidOperationException)
        {
            return method.Invoke;
        }
    }

    private static Func<object?, ValueTask<object?>> CreateReturnReader(Type returnType)
    {
        if (returnType == typeof(void))
        {
            return static _ => ValueTask.FromResult<object?>(null);
        }

        if (returnType == typeof(ValueTask))
        {
            return ReadValueTaskAsync;
        }

        if (returnType == typeof(Task))
        {
            return ReadTaskAsync;
        }

        if (IsGenericValueTask(returnType))
        {
            return (Func<object?, ValueTask<object?>>)ReadGenericValueTaskMethod
                .MakeGenericMethod(returnType.GetGenericArguments()[0])
                .CreateDelegate(typeof(Func<object?, ValueTask<object?>>));
        }

        if (IsGenericTask(returnType))
        {
            return (Func<object?, ValueTask<object?>>)ReadGenericTaskMethod
                .MakeGenericMethod(returnType.GetGenericArguments()[0])
                .CreateDelegate(typeof(Func<object?, ValueTask<object?>>));
        }

        return static result => ValueTask.FromResult(result);
    }

    private static async ValueTask<object?> ReadValueTaskAsync(object? result)
    {
        await ((ValueTask)result!).ConfigureAwait(false);
        return null;
    }

    private static async ValueTask<object?> ReadTaskAsync(object? result)
    {
        await ((Task)result!).ConfigureAwait(false);
        return null;
    }

    private static async ValueTask<object?> ReadGenericValueTaskAsync<T>(object? result)
        => await ((ValueTask<T>)result!).ConfigureAwait(false);

    private static async ValueTask<object?> ReadGenericTaskAsync<T>(object? result)
        => await ((Task<T>)result!).ConfigureAwait(false);

    private static bool IsGenericTask(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>);

    private static bool IsGenericValueTask(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>);
}
