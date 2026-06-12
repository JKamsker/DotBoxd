namespace SafeIR.Verifier;

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

public sealed partial class GeneratedAssemblyVerifier
{
    private static void VerifyTypeSurface(
        MetadataReader reader,
        TypeDefinition type,
        List<VerificationDiagnostic> diagnostics)
    {
        GeneratedNameVerifier.VerifyTypeName(reader, type, diagnostics);
        var visibility = type.Attributes & TypeAttributes.VisibilityMask;
        if (visibility != TypeAttributes.Public)
        {
            diagnostics.Add(new VerificationDiagnostic("V-PUBLIC-SURFACE", "generated type must be public"));
        }

        if ((type.Attributes & (TypeAttributes.Abstract | TypeAttributes.Sealed)) !=
            (TypeAttributes.Abstract | TypeAttributes.Sealed))
        {
            var name = reader.GetString(type.Name);
            diagnostics.Add(new VerificationDiagnostic(
                "V-TYPE-SHAPE",
                $"generated type '{name}' must be static"));
        }

        if ((type.Attributes & TypeAttributes.Interface) != 0)
        {
            diagnostics.Add(new VerificationDiagnostic("V-TYPE-SHAPE", "generated type must not be an interface"));
        }
    }

    private static void VerifyFields(MetadataReader reader, TypeDefinition type, List<VerificationDiagnostic> diagnostics)
    {
        foreach (var fieldHandle in type.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            diagnostics.Add(new VerificationDiagnostic("V-FIELD", "generated fields are not allowed"));
            if ((field.Attributes & FieldAttributes.Static) != 0 &&
                (field.Attributes & (FieldAttributes.InitOnly | FieldAttributes.Literal)) == 0)
            {
                diagnostics.Add(new VerificationDiagnostic("V-FIELD-STATIC", "mutable static fields are not allowed"));
            }
        }
    }

    private static void VerifyMethods(
        PEReader peReader,
        MetadataReader reader,
        VerificationPolicy policy,
        TypeDefinition type,
        List<VerificationDiagnostic> diagnostics)
    {
        var executeMethods = 0;
        foreach (var methodHandle in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            var name = reader.GetString(method.Name);
            VerifyMethodSurface(reader, method, name, diagnostics);
            GenericParameterVerifier.VerifyMethod(reader, method, name, diagnostics);
            VerifyMethodImplementation(method, name, diagnostics);
            if (name == "Execute" && (method.Attributes & MethodAttributes.Public) != 0)
            {
                executeMethods++;
            }

            if (name is ".cctor" or "Finalize")
            {
                diagnostics.Add(new VerificationDiagnostic("V-METHOD-SPECIAL", $"method '{name}' is not allowed"));
            }

            if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
            {
                diagnostics.Add(new VerificationDiagnostic("V-METHOD-PINVOKE", $"method '{name}' has P/Invoke attributes"));
            }

            if (method.RelativeVirtualAddress != 0)
            {
                var body = peReader.GetMethodBody(method.RelativeVirtualAddress);
                OpCodeVerifier.VerifyBody(reader, policy, body, diagnostics);
                GeneratedMethodShapeVerifier.VerifyBody(reader, method, body, name, diagnostics);
            }
        }

        if (executeMethods != 1)
        {
            diagnostics.Add(new VerificationDiagnostic(
                "V-PUBLIC-SURFACE",
                "generated type must expose exactly one public Execute method"));
        }
    }

    private static void VerifyMethodSurface(
        MetadataReader reader,
        MethodDefinition method,
        string name,
        List<VerificationDiagnostic> diagnostics)
    {
        if (!GeneratedNameVerifier.IsAllowedMethodName(name))
        {
            diagnostics.Add(new VerificationDiagnostic(
                "V-METHOD-NAME",
                $"method '{name}' is not an expected generated method name"));
        }

        var access = method.Attributes & MethodAttributes.MemberAccessMask;
        if (name == "Execute")
        {
            if (access != MethodAttributes.Public || (method.Attributes & MethodAttributes.Static) == 0)
            {
                diagnostics.Add(new VerificationDiagnostic(
                    "V-PUBLIC-SURFACE",
                    "Execute must be public and static"));
            }

            VerifyExecuteSignature(reader, method, diagnostics);
            return;
        }

        if (access != MethodAttributes.Private)
        {
            diagnostics.Add(new VerificationDiagnostic(
                "V-PUBLIC-SURFACE",
                $"method '{name}' is not part of the public surface"));
        }

        if ((method.Attributes & MethodAttributes.Static) == 0)
        {
            diagnostics.Add(new VerificationDiagnostic("V-METHOD-ATTR", $"method '{name}' must be static"));
        }
    }

    private static void VerifyExecuteSignature(
        MetadataReader reader,
        MethodDefinition method,
        List<VerificationDiagnostic> diagnostics)
    {
        var signature = method.DecodeSignature(MethodSignatureNameProvider.Instance, genericContext: null);
        if (signature.ReturnType != "SafeIR.SandboxValue" ||
            signature.ParameterTypes.Length != 2 ||
            signature.ParameterTypes[0] != "SafeIR.SandboxContext" ||
            signature.ParameterTypes[1] != "SafeIR.SandboxValue")
        {
            diagnostics.Add(new VerificationDiagnostic(
                "V-EXECUTE-SIGNATURE",
                "Execute must match SandboxValue Execute(SandboxContext, SandboxValue)"));
        }
    }

    private static void VerifyMethodImplementation(
        MethodDefinition method,
        string name,
        List<VerificationDiagnostic> diagnostics)
    {
        var impl = method.ImplAttributes;
        var codeType = impl & MethodImplAttributes.CodeTypeMask;
        if ((impl & (MethodImplAttributes.InternalCall | MethodImplAttributes.Synchronized | MethodImplAttributes.Unmanaged)) != 0 ||
            codeType is MethodImplAttributes.Native or MethodImplAttributes.Runtime)
        {
            diagnostics.Add(new VerificationDiagnostic(
                "V-METHOD-ATTR",
                $"method '{name}' uses unsupported implementation attributes"));
        }
    }
}
