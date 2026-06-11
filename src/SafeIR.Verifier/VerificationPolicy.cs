namespace SafeIR.Verifier;

using SafeIR;

public sealed record VerificationPolicy(
    IReadOnlySet<string> AllowedAssemblies,
    IReadOnlySet<string> AllowedAssemblyIdentities,
    IReadOnlySet<string> AllowedTypes,
    IReadOnlySet<string> AllowedMembers,
    IReadOnlySet<string> ForbiddenTypePrefixes,
    string VerifierVersion)
{
    private const string Context = "SafeIR.SandboxContext";
    private const string Value = "SafeIR.SandboxValue";
    private const string ValueArray = "SafeIR.SandboxValue[]";
    private const string SandboxType = "SafeIR.SandboxType";
    private const string Runtime = "SafeIR.Runtime.CompiledRuntime";
    private const string Void = "System.Void";
    private const string Boolean = "System.Boolean";
    private const string Int32 = "System.Int32";
    private const string Double = "System.Double";
    private const string String = "System.String";

    public static VerificationPolicy BoxedValueDefaults()
        => new(
            new HashSet<string>(StringComparer.Ordinal) {
                "System.Private.CoreLib", "System.Runtime", "SafeIR.Core", "SafeIR.Runtime"
            },
            new HashSet<string>(StringComparer.Ordinal) {
                AssemblyIdentity("System.Private.CoreLib", "10.0.0.0", "neutral", "7cec85d7bea7798e"),
                AssemblyIdentity("System.Runtime", "10.0.0.0", "neutral", "b03f5f7f11d50a3a"),
                AssemblyIdentity("SafeIR.Core", SafeIrAssemblyVersion(), "neutral", "null"),
                AssemblyIdentity("SafeIR.Runtime", SafeIrAssemblyVersion(), "neutral", "null")
            },
            new HashSet<string>(StringComparer.Ordinal) {
                "System.Object", "System.Void", "System.Boolean", "System.Int32", "System.String",
                "System.Double",
                "SafeIR.SandboxValue", "SafeIR.SandboxContext", "SafeIR.SandboxType", "SafeIR.Runtime.CompiledRuntime"
            },
            new HashSet<string>(StringComparer.Ordinal) {
                RuntimeMember("ChargeFuel", $"{Context},{Int32}", Void),
                RuntimeMember("EnterCall", Context, Void),
                RuntimeMember("ExitCall", Context, Void),
                RuntimeMember("ValidateEntrypointInput", $"{Value},{Int32}", Void),
                RuntimeMember("GetInputArgument", $"{Value},{Int32},{Int32},{SandboxType}", Value),
                RuntimeMember("RequireValueType", $"{Value},{SandboxType}", Value),
                RuntimeMember("I32", Int32, Value),
                RuntimeMember("F64", Double, Value),
                RuntimeMember("Bool", Boolean, Value),
                RuntimeMember("TypeScalar", String, SandboxType),
                RuntimeMember("TypeList", SandboxType, SandboxType),
                RuntimeMember("TypeMap", $"{SandboxType},{SandboxType}", SandboxType),
                RuntimeMember("StringConst", $"{Context},{String}", Value),
                RuntimeMember("AsI32", Value, Int32),
                RuntimeMember("AsBool", Value, Boolean),
                RuntimeMember("AsF64", Value, Double),
                RuntimeMember("AddI32", $"{Value},{Value}", Value),
                RuntimeMember("SubI32", $"{Value},{Value}", Value),
                RuntimeMember("MulI32", $"{Value},{Value}", Value),
                RuntimeMember("DivI32", $"{Value},{Value}", Value),
                RuntimeMember("RemI32", $"{Value},{Value}", Value),
                RuntimeMember("NegI32", Value, Value),
                RuntimeMember("NotBool", Value, Value),
                RuntimeMember("Eq", $"{Value},{Value}", Value),
                RuntimeMember("Ne", $"{Value},{Value}", Value),
                RuntimeMember("LtI32", $"{Value},{Value}", Value),
                RuntimeMember("LteI32", $"{Value},{Value}", Value),
                RuntimeMember("GtI32", $"{Value},{Value}", Value),
                RuntimeMember("GteI32", $"{Value},{Value}", Value),
                RuntimeMember("And", $"{Value},{Value}", Value),
                RuntimeMember("Or", $"{Value},{Value}", Value),
                RuntimeMember("StringLength", Value, Value),
                RuntimeMember("ConcatString", $"{Context},{Value},{Value}", Value),
                RuntimeMember("AbsI32", Value, Value),
                RuntimeMember("MinI32", $"{Value},{Value}", Value),
                RuntimeMember("MaxI32", $"{Value},{Value}", Value),
                RuntimeMember("ClampI32", $"{Value},{Value},{Value}", Value),
                RuntimeMember("SqrtF64", Value, Value),
                RuntimeMember("FloorF64", Value, Value),
                RuntimeMember("CeilF64", Value, Value),
                RuntimeMember("RoundF64", Value, Value),
                RuntimeMember("ListOf", $"{Context},{ValueArray}", Value),
                RuntimeMember("ListCount", Value, Value),
                RuntimeMember("ListGet", $"{Value},{Value}", Value),
                RuntimeMember("ListAdd", $"{Context},{Value},{Value}", Value),
                RuntimeMember("MapEmpty", $"{Context},{SandboxType},{SandboxType}", Value),
                RuntimeMember("MapContainsKey", $"{Value},{Value}", Value),
                RuntimeMember("MapGet", $"{Value},{Value}", Value),
                RuntimeMember("MapSet", $"{Context},{Value},{Value},{Value}", Value),
                RuntimeMember("MapRemove", $"{Context},{Value},{Value}", Value),
                RuntimeMember("CallBinding", $"{Context},{String},{ValueArray}", Value)
            },
            new HashSet<string>(StringComparer.Ordinal) {
                "System.IO.", "System.Net.", "System.Reflection.", "System.Runtime.Loader.",
                "System.Runtime.InteropServices.", "System.Diagnostics.", "System.Threading.",
                "System.Threading.Tasks.", "System.Activator", "System.Environment",
                "System.GC", "System.Delegate", "System.Linq.Expressions.", "Microsoft.CSharp."
            },
            "safe-ir-verifier-2");

    public bool IsMemberAllowed(string memberSignature) => AllowedMembers.Contains(memberSignature);

    private static string RuntimeMember(string name, string parameters, string returnType)
        => $"{Runtime}.{name}({parameters}):{returnType}";

    private static string SafeIrAssemblyVersion()
        => typeof(SandboxValue).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    private static string AssemblyIdentity(string name, string version, string culture, string publicKeyToken)
        => $"{name}, Version={version}, Culture={culture}, PublicKeyToken={publicKeyToken}";
}
