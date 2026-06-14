namespace DotBoxd.Kernels;

public static class EntrypointBinder
{
    public static IReadOnlyList<SandboxValue> BindArguments(SandboxFunction function, SandboxValue input)
    {
        ValidateInputShape(input, function.Parameters.Count);
        var args = new SandboxValue[function.Parameters.Count];
        for (var i = 0; i < args.Length; i++) {
            args[i] = GetArgument(input, i, args.Length, function.Parameters[i].Type);
        }

        return args;
    }

    public static void ValidateInputShape(SandboxValue input, int parameterCount)
    {
        if (parameterCount == 0) {
            RequireType(input, SandboxType.Unit, "entrypoint input argument mismatch");
            return;
        }

        if (parameterCount == 1) {
            return;
        }

        if (input is not ListValue list || list.Values.Count != parameterCount) {
            throw InvalidInput("entrypoint input argument mismatch");
        }
    }

    public static SandboxValue GetArgument(
        SandboxValue input,
        int index,
        int parameterCount,
        SandboxType expectedType)
    {
        if (index < 0 || index >= parameterCount) {
            throw InvalidInput("entrypoint input argument mismatch");
        }

        var value = parameterCount == 1
            ? input
            : ((ListValue)input).Values[index];
        RequireType(value, expectedType, "entrypoint input argument mismatch");
        return value;
    }

    public static void RequireType(SandboxValue value, SandboxType expectedType, string message)
    {
        SandboxValueValidator.RequireType(value, expectedType, message);
    }

    private static SandboxRuntimeException InvalidInput(string message)
        => new(new SandboxError(SandboxErrorCode.InvalidInput, message));
}
