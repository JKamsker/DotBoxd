using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime;

public static partial class CompiledRuntime
{
    public static SandboxType TypeScalar(string name)
        => name switch
        {
            "Unit" => SandboxType.Unit,
            "Bool" => SandboxType.Bool,
            "I32" => SandboxType.I32,
            "I64" => SandboxType.I64,
            "F64" => SandboxType.F64,
            "String" => SandboxType.String,
            "SandboxPath" => SandboxType.SandboxPath,
            "SandboxUri" => SandboxType.SandboxUri,
            _ => SandboxType.Scalar(name)
        };

    public static SandboxType TypeList(SandboxType itemType) => SandboxType.List(itemType);

    public static SandboxType TypeMap(SandboxType keyType, SandboxType valueType) => SandboxType.Map(keyType, valueType);

    public static SandboxType TypeRecord(SandboxType[] fieldTypes) => SandboxType.Record(fieldTypes);

    public static SandboxType[] CreateMeteredTypeArray(SandboxContext context, int count)
    {
        ChargeTypeArray(context, count);
        return new SandboxType[count];
    }

    public static SandboxType[] CreateTypeArray(int count)
        => count >= 0 ? new SandboxType[count] : throw InvalidInput("type array length must be non-negative");

    private static void ChargeTypeArray(SandboxContext context, int count)
    {
        if (count < 0)
        {
            throw InvalidInput("type array length must be non-negative");
        }

        var elementCount = Math.Max(1L, count);
        context.ChargeFuel(elementCount);
        context.ChargeAllocation(checked(elementCount * 8));
    }
}
