namespace SafeIR.Verifier;

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

public sealed partial class GeneratedAssemblyVerifier : IGeneratedAssemblyVerifier
{
    public ValueTask<VerificationResult> VerifyAsync(
        ReadOnlyMemory<byte> assemblyBytes,
        ArtifactManifest manifest,
        VerificationPolicy policy,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<VerificationDiagnostic>();
        var assemblyHash = Convert.ToHexString(SHA256.HashData(assemblyBytes.Span)).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(manifest.AssemblyHash) &&
            !StringComparer.Ordinal.Equals(manifest.AssemblyHash, assemblyHash))
        {
            diagnostics.Add(new VerificationDiagnostic("V-MANIFEST-HASH", "assembly hash does not match manifest"));
        }

        ManifestIdentityVerifier.Verify(manifest, policy, diagnostics);

        try
        {
            using var stream = new MemoryStream(assemblyBytes.ToArray(), writable: false);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                diagnostics.Add(new VerificationDiagnostic("V-PE-METADATA", "assembly has no CLR metadata"));
            }
            else
            {
                VerifyMetadata(peReader, peReader.GetMetadataReader(), policy, diagnostics, cancellationToken);
            }
        }
        catch (BadImageFormatException ex)
        {
            diagnostics.Add(new VerificationDiagnostic("V-PE-FORMAT", ex.Message));
        }

        return ValueTask.FromResult(new VerificationResult(
            diagnostics.Count == 0,
            diagnostics,
            assemblyHash,
            policy.VerifierVersion,
            DateTimeOffset.UtcNow));
    }

    private static void VerifyMetadata(
        PEReader peReader,
        MetadataReader reader,
        VerificationPolicy policy,
        List<VerificationDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        PeStructureVerifier.Verify(peReader, diagnostics);
        VerifyAssemblyReferences(reader, policy, diagnostics);
        VerifyTypeReferences(reader, policy, diagnostics);
        VerifyCustomAttributes(reader, diagnostics);
        MetadataTableVerifier.Verify(reader, diagnostics);
        VerifyDefinitions(peReader, reader, policy, diagnostics, cancellationToken);
        if (reader.ManifestResources.Count > 0)
        {
            diagnostics.Add(new VerificationDiagnostic("V-RESOURCE", "embedded resources are not allowed"));
        }

        if (reader.GetTableRowCount(TableIndex.ImplMap) > 0)
        {
            diagnostics.Add(new VerificationDiagnostic("V-PINVOKE", "P/Invoke metadata is not allowed"));
        }
    }

    private static void VerifyAssemblyReferences(
        MetadataReader reader,
        VerificationPolicy policy,
        List<VerificationDiagnostic> diagnostics)
    {
        foreach (var handle in reader.AssemblyReferences)
        {
            var reference = reader.GetAssemblyReference(handle);
            var name = reader.GetString(reference.Name);
            var identity = AssemblyReferenceIdentity.Format(reader, reference);
            if (!policy.AllowedAssemblyIdentities.Contains(identity))
            {
                diagnostics.Add(new VerificationDiagnostic("V-ASM-REF", $"assembly reference '{identity}' is not allowed"));
            }
        }
    }

    private static void VerifyTypeReferences(
        MetadataReader reader,
        VerificationPolicy policy,
        List<VerificationDiagnostic> diagnostics)
    {
        foreach (var handle in reader.TypeReferences)
        {
            var name = MetadataName.TypeReference(reader, handle);
            if (policy.ForbiddenTypePrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            {
                diagnostics.Add(new VerificationDiagnostic("V-TYPE-FORBIDDEN", $"type reference '{name}' is forbidden"));
            }
            else if (!policy.AllowedTypes.Contains(name))
            {
                diagnostics.Add(new VerificationDiagnostic("V-TYPE-REF", $"type reference '{name}' is not allowed"));
            }
        }
    }

    private static void VerifyCustomAttributes(
        MetadataReader reader,
        List<VerificationDiagnostic> diagnostics)
    {
        foreach (var handle in reader.CustomAttributes)
        {
            var attribute = reader.GetCustomAttribute(handle);
            var member = MetadataName.Member(reader, attribute.Constructor);
            diagnostics.Add(new VerificationDiagnostic(
                "V-CUSTOM-ATTR",
                $"custom attribute '{member.TypeName}.{member.MemberName}' is not allowed"));
        }
    }

    private static void VerifyDefinitions(
        PEReader peReader,
        MetadataReader reader,
        VerificationPolicy policy,
        List<VerificationDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var generatedTypeCount = 0;
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = reader.GetTypeDefinition(typeHandle);
            if (IsModuleType(reader, type))
            {
                VerifyModuleSurface(type, diagnostics);
                continue;
            }

            generatedTypeCount++;
            VerifyTypeSurface(reader, type, diagnostics);
            GenericParameterVerifier.VerifyType(reader, type, diagnostics);
            VerifyFields(reader, type, diagnostics);
            VerifyMethods(peReader, reader, policy, type, diagnostics);
        }

        if (generatedTypeCount != 1)
        {
            diagnostics.Add(new VerificationDiagnostic(
                "V-PUBLIC-SURFACE",
                "generated assembly must define exactly one generated type"));
        }
    }

    private static bool IsModuleType(MetadataReader reader, TypeDefinition type)
        => reader.GetString(type.Name) == "<Module>";

    private static void VerifyModuleSurface(TypeDefinition type, List<VerificationDiagnostic> diagnostics)
    {
        if (type.GetMethods().Count > 0)
        {
            diagnostics.Add(new VerificationDiagnostic("V-MODULE-SURFACE", "module-level methods are not allowed"));
        }

        if (type.GetFields().Count > 0)
        {
            diagnostics.Add(new VerificationDiagnostic("V-MODULE-SURFACE", "module-level fields are not allowed"));
        }
    }

}
