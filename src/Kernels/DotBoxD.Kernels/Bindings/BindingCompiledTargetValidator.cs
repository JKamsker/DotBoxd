using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Bindings;

internal static class BindingCompiledTargetValidator
{
    private const string RuntimeStubKind = "RuntimeStub";
    private const string ApprovedCompiledRuntimeType = "DotBoxD.Kernels.Runtime.CompiledRuntime";
    private const string GenericBindingStub = "CallBinding";

    private static readonly HashSet<string> ApprovedCompiledRuntimeMethods = new(StringComparer.Ordinal) {
        GenericBindingStub,
        "Int32ToStringInvariant",
        "StringLength",
        "ConcatString",
        "AbsI32",
        "MinI32",
        "MaxI32",
        "ClampI32",
        "SqrtF64",
        "FloorF64",
        "CeilF64",
        "RoundF64"
    };

    public static void Validate(BindingDescriptor binding, List<SandboxDiagnostic> diagnostics)
    {
        if (binding.Compiled.Kind != RuntimeStubKind)
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COMPILED", $"binding '{binding.Id}' has unsupported compiled target kind"));
        }

        if (string.IsNullOrWhiteSpace(binding.Compiled.Type) ||
            string.IsNullOrWhiteSpace(binding.Compiled.Method))
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COMPILED", $"binding '{binding.Id}' has an incomplete compiled target"));
            return;
        }

        if (binding.Compiled.Type != ApprovedCompiledRuntimeType ||
            !ApprovedCompiledRuntimeMethods.Contains(binding.Compiled.Method))
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COMPILED", $"binding '{binding.Id}' points compiled code outside the approved runtime stub surface"));
            return;
        }

        if (binding.Compiled.Method != GenericBindingStub && binding.Safety != BindingSafety.PureIntrinsic)
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COMPILED", $"binding '{binding.Id}' uses a direct compiled runtime method but is not a pure intrinsic"));
        }

        if (binding.Compiled.Method != GenericBindingStub && binding.AuditLevel != AuditLevel.None)
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COMPILED", $"binding '{binding.Id}' uses a direct compiled runtime method but requires binding audit"));
        }

        ValidateDirectCompiledSignature(binding, diagnostics);
    }

    private static void ValidateDirectCompiledSignature(BindingDescriptor binding, List<SandboxDiagnostic> diagnostics)
    {
        if (binding.Compiled.Method == GenericBindingStub)
        {
            return;
        }

        var expected = DirectCompiledSignature(binding.Compiled.Method);
        if (!binding.ReturnType.Equals(expected.Return) ||
            binding.Parameters.Count != expected.Parameters.Length)
        {
            diagnostics.Add(DirectSignatureDiagnostic(binding));
            return;
        }

        for (var i = 0; i < expected.Parameters.Length; i++)
        {
            if (!binding.Parameters[i].Equals(expected.Parameters[i]))
            {
                diagnostics.Add(DirectSignatureDiagnostic(binding));
                return;
            }
        }
    }

    private static (SandboxType Return, SandboxType[] Parameters) DirectCompiledSignature(string method)
        => method switch
        {
            "Int32ToStringInvariant" => (SandboxType.String, [SandboxType.I32]),
            "StringLength" => (SandboxType.I32, [SandboxType.String]),
            "ConcatString" => (SandboxType.String, [SandboxType.String, SandboxType.String]),
            "AbsI32" => (SandboxType.I32, [SandboxType.I32]),
            "MinI32" or "MaxI32" => (SandboxType.I32, [SandboxType.I32, SandboxType.I32]),
            "ClampI32" => (SandboxType.I32, [SandboxType.I32, SandboxType.I32, SandboxType.I32]),
            "SqrtF64" or "FloorF64" or "CeilF64" or "RoundF64" => (SandboxType.F64, [SandboxType.F64]),
            _ => (SandboxType.Unit, [])
        };

    private static SandboxDiagnostic DirectSignatureDiagnostic(BindingDescriptor binding)
        => new(
            "E-BINDING-COMPILED",
            $"binding '{binding.Id}' direct compiled runtime signature does not match the binding shape");
}
