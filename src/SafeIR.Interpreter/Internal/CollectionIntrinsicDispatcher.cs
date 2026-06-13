namespace SafeIR.Interpreter.Internal;

using SafeIR;

/// <summary>
/// Direct, array-free dispatch for the fixed-arity collection intrinsics
/// (<c>list.*</c> / <c>map.*</c> except the variadic <c>list.of</c>).
///
/// These intrinsics complete synchronously, read their operands positionally, and
/// never let an argument escape into host code, so when the operands are already
/// evaluated they can be handed to <see cref="CollectionOperations"/> straight from
/// locals. That avoids the per-call <c>SandboxValue[]</c> the general call path would
/// otherwise allocate for cheap collection calls inside loops (PAL-0038). The operand
/// ordering and charged operations match <see cref="ExpressionEvaluator"/>'s
/// array-backed path exactly, so observable behavior is unchanged.
/// </summary>
internal static class CollectionIntrinsicDispatcher
{
    /// <summary>
    /// Returns the fixed arity of a collection intrinsic, or <c>-1</c> when the call is
    /// not a fixed-arity collection intrinsic eligible for array-free dispatch. The
    /// variadic <c>list.of</c> is intentionally excluded: it still flows through the
    /// general array path because it must observe the exact argument count.
    /// </summary>
    public static int FixedArity(string name)
        => name switch
        {
            "list.empty" => 0,
            "map.empty" => 0,
            "list.count" => 1,
            "list.get" => 2,
            "list.add" => 2,
            "map.containsKey" => 2,
            "map.get" => 2,
            "map.set" => 3,
            "map.remove" => 2,
            _ => -1
        };

    /// <summary>
    /// Dispatches a fixed-arity collection intrinsic from already-evaluated operands.
    /// <paramref name="arg0"/> through <paramref name="arg2"/> are the operands in
    /// source order (the same order the array path fills them); unused slots are ignored.
    /// </summary>
    public static SandboxValue Dispatch(
        CallExpression call,
        SandboxValue arg0,
        SandboxValue arg1,
        SandboxValue arg2,
        SandboxContext context)
        => call.Name switch
        {
            "list.empty" => CollectionOperations.CreateList(call.GenericType ?? SandboxType.Unit, context),
            "list.count" => CollectionOperations.CountList(arg0, context),
            "list.get" => CollectionOperations.GetListItem(arg1, arg0, context),
            "list.add" => CollectionOperations.AddListItem(arg1, arg0, context),
            "map.empty" => CollectionOperations.CreateMap(
                call.GenericType ?? SandboxType.Map(SandboxType.Unit, SandboxType.Unit),
                context),
            "map.containsKey" => CollectionOperations.ContainsMapKey(arg1, arg0, context),
            "map.get" => CollectionOperations.GetMapValue(arg1, arg0, context),
            "map.set" => CollectionOperations.SetMapValue(arg2, arg1, arg0, context),
            "map.remove" => CollectionOperations.RemoveMapValue(arg1, arg0, context),
            _ => throw new SandboxRuntimeException(
                new SandboxError(SandboxErrorCode.ValidationError, $"'{call.Name}' is not a fixed-arity collection intrinsic"))
        };
}
