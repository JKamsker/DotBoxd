namespace DotBoxd.Kernels.Verifier;

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal static class GeneratedMethodSignatureReader
{
    public static GeneratedMethodSignature Read(
        MetadataReader reader,
        MethodDefinition method,
        MethodBodyBlock body)
    {
        var signature = method.DecodeSignature(MethodSignatureNameProvider.Instance, genericContext: null);
        var arguments = signature.ParameterTypes.ToList();
        if ((method.Attributes & MethodAttributes.Static) == 0)
        {
            arguments.Insert(0, "this");
        }

        return new GeneratedMethodSignature(
            arguments,
            ReadLocals(reader, body.LocalSignature),
            signature.ReturnType);
    }

    private static IReadOnlyList<string> ReadLocals(
        MetadataReader reader,
        StandaloneSignatureHandle localSignature)
    {
        if (localSignature.IsNil)
        {
            return [];
        }

        return reader
            .GetStandaloneSignature(localSignature)
            .DecodeLocalSignature(MethodSignatureNameProvider.Instance, genericContext: null)
            .ToArray();
    }
}

internal sealed record GeneratedMethodSignature(
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> Locals,
    string ReturnType);
