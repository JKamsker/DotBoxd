namespace SafeIR.Verifier;

using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using SafeIR;
using SafeIR.Runtime;
using static SafeIR.Verifier.VerifierTypeNames;

public sealed record VerificationPolicy(
    IReadOnlySet<string> AllowedAssemblies,
    IReadOnlySet<string> AllowedAssemblyIdentities,
    IReadOnlySet<string> AllowedTypes,
    IReadOnlySet<string> AllowedMembers,
    IReadOnlySet<string> ForbiddenTypePrefixes,
    IReadOnlySet<string> RuntimeFacadeIdentities,
    string VerifierVersion,
    VerificationManifestIdentity? ExpectedManifestIdentity = null)
{
    private IReadOnlySet<string> _allowedAssemblies = Freeze(AllowedAssemblies);
    private IReadOnlySet<string> _allowedAssemblyIdentities = Freeze(AllowedAssemblyIdentities);
    private IReadOnlySet<string> _allowedTypes = Freeze(AllowedTypes);
    private IReadOnlySet<string> _allowedMembers = Freeze(AllowedMembers);
    private IReadOnlySet<string> _forbiddenTypePrefixes = Freeze(ForbiddenTypePrefixes);
    private IReadOnlySet<string> _runtimeFacadeIdentities = Freeze(RuntimeFacadeIdentities);

    public IReadOnlySet<string> AllowedAssemblies { get => _allowedAssemblies; init => _allowedAssemblies = Freeze(value); }
    public IReadOnlySet<string> AllowedAssemblyIdentities { get => _allowedAssemblyIdentities; init => _allowedAssemblyIdentities = Freeze(value); }
    public IReadOnlySet<string> AllowedTypes { get => _allowedTypes; init => _allowedTypes = Freeze(value); }
    public IReadOnlySet<string> AllowedMembers { get => _allowedMembers; init => _allowedMembers = Freeze(value); }
    public IReadOnlySet<string> ForbiddenTypePrefixes { get => _forbiddenTypePrefixes; init => _forbiddenTypePrefixes = Freeze(value); }
    public IReadOnlySet<string> RuntimeFacadeIdentities { get => _runtimeFacadeIdentities; init => _runtimeFacadeIdentities = Freeze(value); }

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
                ObjectName, VoidName, BooleanName, Int32Name, Int64Name, StringName, DoubleName,
                SandboxValueName, SandboxContextName, SandboxTypeName, CompiledRuntimeName
            },
            new HashSet<string>(StringComparer.Ordinal) {
                RuntimeMember("ChargeFuel", $"{SandboxContextName},{Int32Name}", VoidName),
                RuntimeMember("ChargeLoopIteration", $"{SandboxContextName},{Int32Name}", VoidName),
                RuntimeMember("EnterCall", SandboxContextName, VoidName),
                RuntimeMember("ExitCall", SandboxContextName, VoidName),
                RuntimeMember("ValidateEntrypointInput", $"{SandboxValueName},{Int32Name}", VoidName),
                RuntimeMember("GetInputArgument", $"{SandboxValueName},{Int32Name},{Int32Name},{SandboxTypeName}", SandboxValueName),
                RuntimeMember("RequireValueType", $"{SandboxValueName},{SandboxTypeName}", SandboxValueName),
                RuntimeMember("Unit", "", SandboxValueName),
                RuntimeMember("I32", Int32Name, SandboxValueName),
                RuntimeMember("I64", Int64Name, SandboxValueName),
                RuntimeMember("F64", DoubleName, SandboxValueName),
                RuntimeMember("Bool", BooleanName, SandboxValueName),
                RuntimeMember("TypeScalar", StringName, SandboxTypeName),
                RuntimeMember("TypeList", SandboxTypeName, SandboxTypeName),
                RuntimeMember("TypeMap", $"{SandboxTypeName},{SandboxTypeName}", SandboxTypeName),
                RuntimeMember("TypeRecord", SandboxTypeArrayName, SandboxTypeName),
                RuntimeMember("CreateTypeArray", Int32Name, SandboxTypeArrayName),
                RuntimeMember("StringConst", $"{SandboxContextName},{StringName}", SandboxValueName),
                RuntimeMember("OpaqueIdConst", $"{SandboxContextName},{StringName},{StringName}", SandboxValueName),
                RuntimeMember("PathConst", $"{SandboxContextName},{StringName}", SandboxValueName),
                RuntimeMember("UriConst", $"{SandboxContextName},{StringName}", SandboxValueName),
                RuntimeMember("StringLiteralValue", StringName, SandboxValueName),
                RuntimeMember("OpaqueIdLiteralValue", $"{StringName},{StringName}", SandboxValueName),
                RuntimeMember("PathLiteralValue", StringName, SandboxValueName),
                RuntimeMember("UriLiteralValue", StringName, SandboxValueName),
                RuntimeMember("AsI32", SandboxValueName, Int32Name),
                RuntimeMember("AsI64", SandboxValueName, Int64Name),
                RuntimeMember("AsBool", SandboxValueName, BooleanName),
                RuntimeMember("AsF64", SandboxValueName, DoubleName),
                RuntimeMember("AddI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("SubI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("MulI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("DivI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("RemI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("NegI32", SandboxValueName, SandboxValueName),
                RuntimeMember("Neg", SandboxValueName, SandboxValueName),
                RuntimeMember("Add", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Sub", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Mul", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Div", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Rem", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("NotBool", SandboxValueName, SandboxValueName),
                RuntimeMember("Eq", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Ne", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("LtI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("LteI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("GtI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("GteI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Lt", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Lte", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Gt", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Gte", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("And", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Or", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("StringLength", SandboxValueName, SandboxValueName),
                RuntimeMember("ConcatString", $"{SandboxContextName},{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("AbsI32", SandboxValueName, SandboxValueName),
                RuntimeMember("MinI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("MaxI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("ClampI32", $"{SandboxValueName},{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("SqrtF64", SandboxValueName, SandboxValueName),
                RuntimeMember("FloorF64", SandboxValueName, SandboxValueName),
                RuntimeMember("CeilF64", SandboxValueName, SandboxValueName),
                RuntimeMember("RoundF64", SandboxValueName, SandboxValueName),
                RuntimeMember("CreateValueArray", $"{SandboxContextName},{Int32Name}", SandboxValueArrayName),
                RuntimeMember("CreateLiteralValueArray", Int32Name, SandboxValueArrayName),
                RuntimeMember("ListEmpty", $"{SandboxContextName},{SandboxTypeName}", SandboxValueName),
                RuntimeMember("ListOf", $"{SandboxContextName},{SandboxValueArrayName}", SandboxValueName),
                RuntimeMember("ListLiteral", $"{SandboxContextName},{SandboxTypeName},{SandboxValueArrayName}", SandboxValueName),
                RuntimeMember("ListLiteralValue", $"{SandboxTypeName},{SandboxValueArrayName}", SandboxValueName),
                RuntimeMember("ListCount", $"{SandboxContextName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("ListGet", $"{SandboxContextName},{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("ListAdd", $"{SandboxContextName},{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("MapEmpty", $"{SandboxContextName},{SandboxTypeName},{SandboxTypeName}", SandboxValueName),
                RuntimeMember("MapLiteral", $"{SandboxContextName},{SandboxTypeName},{SandboxTypeName},{SandboxValueArrayName},{SandboxValueArrayName}", SandboxValueName),
                RuntimeMember("MapLiteralValue", $"{SandboxTypeName},{SandboxTypeName},{SandboxValueArrayName},{SandboxValueArrayName}", SandboxValueName),
                RuntimeMember("MapContainsKey", $"{SandboxContextName},{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("MapGet", $"{SandboxContextName},{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("MapSet", $"{SandboxContextName},{SandboxValueName},{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("MapRemove", $"{SandboxContextName},{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("RecordNew", $"{SandboxContextName},{SandboxValueArrayName}", SandboxValueName),
                RuntimeMember("RecordGet", $"{SandboxContextName},{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("CallBinding", $"{SandboxContextName},{StringName},{SandboxValueArrayName}", SandboxValueName)
            },
            new HashSet<string>(StringComparer.Ordinal) {
                "System.IO.", "System.Net.", "System.Reflection.", "System.Runtime.Loader.",
                "System.Runtime.InteropServices.", "System.Diagnostics.", "System.Threading.",
                "System.Threading.Tasks.", "System.Activator", "System.Environment",
                "System.GC", "System.Delegate", "System.IServiceProvider",
                "System.Linq.Expressions.", "Microsoft.CSharp."
            },
            RuntimeFacadeIdentityDefaults(),
            "safe-ir-verifier-7");

    public bool IsMemberAllowed(string memberSignature) => AllowedMembers.Contains(memberSignature);

    public VerificationPolicy WithExpectedManifest(VerificationManifestIdentity identity)
        => this with { ExpectedManifestIdentity = identity };

    public string AllowlistHash
        => Hash(
            "allowlist",
            AllowedAssemblies
                .Concat(AllowedAssemblyIdentities)
                .Concat(AllowedTypes)
                .Concat(AllowedMembers)
                .Concat(ForbiddenTypePrefixes));

    public string RuntimeFacadeHash
        => Hash(
            "runtime-facade",
            AllowedMembers
                .Where(m => m.StartsWith($"{CompiledRuntimeName}.", StringComparison.Ordinal))
                .Concat(RuntimeFacadeIdentities));

    private static string Hash(string prefix, IEnumerable<string> values)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            prefix + "|" + string.Join('|', values.Order(StringComparer.Ordinal)))))
            .ToLowerInvariant();

    private static IReadOnlySet<string> Freeze(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.ToFrozenSet(StringComparer.Ordinal);
    }

    private static string RuntimeMember(string name, string parameters, string returnType)
        => $"{CompiledRuntimeName}.{name}({parameters}):{returnType}";

    private static string SafeIrAssemblyVersion()
        => typeof(SandboxValue).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    private static string AssemblyIdentity(string name, string version, string culture, string publicKeyToken)
        => $"{name}, Version={version}, Culture={culture}, PublicKeyToken={publicKeyToken}";

    private static IReadOnlySet<string> RuntimeFacadeIdentityDefaults()
        => new HashSet<string>(StringComparer.Ordinal) {
            AssemblyModuleIdentity(typeof(SandboxValue).Assembly),
            AssemblyModuleIdentity(typeof(CompiledRuntime).Assembly)
        };

    private static string AssemblyModuleIdentity(System.Reflection.Assembly assembly)
    {
        var name = assembly.GetName();
        return $"{name.Name}, Version={name.Version}, Mvid={assembly.ManifestModule.ModuleVersionId:N}";
    }
}
