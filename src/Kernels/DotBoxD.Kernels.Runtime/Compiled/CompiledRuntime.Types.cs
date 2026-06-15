namespace DotBoxD.Kernels.Runtime;

using DotBoxD.Kernels;

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

    public static SandboxType[] CreateTypeArray(int count)
        => count >= 0 ? new SandboxType[count] : throw InvalidInput("type array length must be non-negative");
}
