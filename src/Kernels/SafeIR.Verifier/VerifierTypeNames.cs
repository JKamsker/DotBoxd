namespace SafeIR.Verifier;

internal static class VerifierTypeNames
{
    public const string ObjectName = "System.Object";
    public const string VoidName = "System.Void";
    public const string BooleanName = "System.Boolean";
    public const string Int32Name = "System.Int32";
    public const string Int64Name = "System.Int64";
    public const string DoubleName = "System.Double";
    public const string StringName = "System.String";
    public const string SandboxContextName = "SafeIR.SandboxContext";
    public const string SandboxValueName = "SafeIR.SandboxValue";
    public const string SandboxValueArrayName = SandboxValueName + "[]";
    public const string SandboxTypeName = "SafeIR.SandboxType";
    public const string SandboxTypeArrayName = SandboxTypeName + "[]";
    public const string CompiledRuntimeName = "SafeIR.Runtime.CompiledRuntime";
}
