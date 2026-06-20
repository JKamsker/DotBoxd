using System.Reflection.Metadata;

namespace DotBoxD.Kernels.Verifier.Generated;

internal static class GenericParameterVerifier
{
    public static void VerifyType(
        MetadataReader reader,
        TypeDefinition type,
        List<VerificationDiagnostic> diagnostics)
        => Verify(reader, type.GetGenericParameters(), "generated type", diagnostics);

    public static void VerifyMethod(
        MetadataReader reader,
        MethodDefinition method,
        string methodName,
        List<VerificationDiagnostic> diagnostics)
        => Verify(reader, method.GetGenericParameters(), $"method '{methodName}'", diagnostics);

    private static void Verify(
        MetadataReader reader,
        GenericParameterHandleCollection parameters,
        string owner,
        List<VerificationDiagnostic> diagnostics)
    {
        foreach (var parameterHandle in parameters)
        {
            var parameter = reader.GetGenericParameter(parameterHandle);
            var name = reader.GetString(parameter.Name);
            diagnostics.Add(new VerificationDiagnostic(
                "V-GENERIC",
                $"{owner} generic parameter '{name}' is not allowed"));
        }
    }
}
