using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Verifier.Diagnostics;

/// <summary>
/// Classifies what a verifier diagnostic most likely indicates, so operators can
/// distinguish a user-fixable generated shape problem from artifact tampering,
/// a host/runtime version mismatch, or a malformed input artifact.
/// </summary>
public enum VerifierDiagnosticCategory
{
    /// <summary>
    /// The artifact bytes do not match the manifest, or the manifest does not match the
    /// expected verification context. Treat as a tampering or cache-poisoning signal.
    /// </summary>
    ArtifactIntegrity,

    /// <summary>
    /// The verification context (manifest identity) does not match the host's compiler,
    /// type-system, runtime, or verifier versions. Usually a release/version mismatch, not user IL.
    /// </summary>
    HostVersionMismatch,

    /// <summary>
    /// The Portable Executable container or CLR metadata is malformed or structurally disallowed.
    /// Either a corrupt/tampered artifact or a non-DotBoxD.Kernels producer.
    /// </summary>
    MalformedArtifact,

    /// <summary>
    /// The generated assembly references types, assemblies, members, or IL that the sandbox
    /// policy forbids. Indicates disallowed capabilities reaching the verifier.
    /// </summary>
    ForbiddenCapability,

    /// <summary>
    /// The generated code does not match the shape the DotBoxD.Kernels compiler is expected to emit
    /// (surface, signatures, stack discipline, control flow). Indicates an unsupported or
    /// non-canonical generated shape.
    /// </summary>
    UnsupportedGeneratedShape,
}

/// <summary>
/// Public reference entry for a single verifier diagnostic <see cref="VerificationDiagnostic.Code"/>.
/// </summary>
/// <param name="Code">The stable <c>V-*</c> diagnostic code.</param>
/// <param name="Category">What the code most likely indicates.</param>
/// <param name="Meaning">A short human-readable description of the rule that was violated.</param>
/// <param name="LikelyCause">The most common reason this code is emitted.</param>
/// <param name="Remediation">Guidance for a consumer or operator investigating the code.</param>
/// <param name="ExpectedFromCompilerOutput">
/// Whether well-formed output from the canonical DotBoxD.Kernels compiler can legitimately produce this code.
/// Codes that are <c>false</c> here should never appear for a trusted, current artifact and are
/// strong tampering or host-mismatch signals.
/// </param>
public sealed record VerifierDiagnosticReference(
    string Code,
    VerifierDiagnosticCategory Category,
    string Meaning,
    string LikelyCause,
    string Remediation,
    bool ExpectedFromCompilerOutput);

/// <summary>
/// Public, maintained reference for the <c>V-*</c> diagnostic codes emitted by
/// <see cref="IGeneratedAssemblyVerifier"/> through <see cref="VerificationResult.Diagnostics"/>.
/// Consumers should reference these constants instead of hard-coding magic strings, and can use
/// <see cref="All"/> / <see cref="TryGetReference"/> to map a code to its documented meaning,
/// likely cause, category, and remediation.
/// </summary>
public static class VerifierDiagnosticCodes
{
    /// <summary>Assembly hash is missing from the manifest or does not match the artifact bytes.</summary>
    public const string ManifestHash = "V-MANIFEST-HASH";

    /// <summary>Manifest identity does not match the expected verification context, or none was supplied.</summary>
    public const string ManifestIdentity = "V-MANIFEST-IDENTITY";

    /// <summary>Portable Executable container is malformed or not a valid DotBoxD.Kernels DLL.</summary>
    public const string PeFormat = "V-PE-FORMAT";

    /// <summary>Assembly has no CLR metadata or CLR header.</summary>
    public const string PeMetadata = "V-PE-METADATA";

    /// <summary>Assembly is mixed-mode or contains native code.</summary>
    public const string PeMixed = "V-PE-MIXED";

    /// <summary>Generated artifact defines an entrypoint, which is not allowed.</summary>
    public const string PeEntrypoint = "V-PE-ENTRYPOINT";

    /// <summary>Assembly contains native PE or CLR header directories.</summary>
    public const string PeNative = "V-PE-NATIVE";

    /// <summary>Assembly contains a disallowed PE section or unsafe section permissions.</summary>
    public const string PeSection = "V-PE-SECTION";

    /// <summary>A method body contained malformed or unreadable IL.</summary>
    public const string IlFormat = "V-IL-FORMAT";

    /// <summary>An assembly reference is not on the policy allow-list.</summary>
    public const string AssemblyReference = "V-ASM-REF";

    /// <summary>A type reference matches a policy-forbidden prefix.</summary>
    public const string TypeForbidden = "V-TYPE-FORBIDDEN";

    /// <summary>A type reference is not on the policy allow-list.</summary>
    public const string TypeReference = "V-TYPE-REF";

    /// <summary>A member (method/field) reference or local call target is not allowed by policy.</summary>
    public const string Member = "V-MEMBER";

    /// <summary>The generated assembly carries a custom attribute, which is not allowed.</summary>
    public const string CustomAttribute = "V-CUSTOM-ATTR";

    /// <summary>The assembly embeds resources, which are not allowed.</summary>
    public const string Resource = "V-RESOURCE";

    /// <summary>The assembly carries P/Invoke (ImplMap) metadata, which is not allowed.</summary>
    public const string PInvoke = "V-PINVOKE";

    /// <summary>An IL opcode used in a method body is not on the allow-list or is explicitly forbidden.</summary>
    public const string OpCode = "V-OPCODE";

    /// <summary>A branch targets an offset that is not a valid instruction boundary.</summary>
    public const string ControlFlow = "V-CONTROL-FLOW";

    /// <summary>A method body declares exception handlers, which are not allowed.</summary>
    public const string Exception = "V-EXCEPTION";

    /// <summary>An array allocation uses an element type other than the allowed DotBoxD.Kernels value type.</summary>
    public const string Array = "V-ARRAY";

    /// <summary>The compiled <c>Execute</c>/function shape does not match the required DotBoxD.Kernels dispatch shape.</summary>
    public const string CompiledShape = "V-COMPILED-SHAPE";

    /// <summary>The public surface is wrong: extra types/methods, missing <c>Execute</c>, or bad visibility/name.</summary>
    public const string PublicSurface = "V-PUBLIC-SURFACE";

    /// <summary>Module-level (&lt;Module&gt;) methods or fields are present, which is not allowed.</summary>
    public const string ModuleSurface = "V-MODULE-SURFACE";

    /// <summary>The generated type is not a static (abstract+sealed) non-interface type.</summary>
    public const string TypeShape = "V-TYPE-SHAPE";

    /// <summary>The generated type declares fields, which is not allowed.</summary>
    public const string Field = "V-FIELD";

    /// <summary>The generated type declares a mutable static field, which is not allowed.</summary>
    public const string FieldStatic = "V-FIELD-STATIC";

    /// <summary>A disallowed special method (such as <c>.cctor</c> or <c>Finalize</c>) is present.</summary>
    public const string MethodSpecial = "V-METHOD-SPECIAL";

    /// <summary>A method carries P/Invoke attributes.</summary>
    public const string MethodPInvoke = "V-METHOD-PINVOKE";

    /// <summary>A generated executable method is missing its method body.</summary>
    public const string MethodBody = "V-METHOD-BODY";

    /// <summary>A method name is not an expected generated method name.</summary>
    public const string MethodName = "V-METHOD-NAME";

    /// <summary>A method uses unsupported attributes (virtual/abstract/synchronized/native/internal-call/non-static).</summary>
    public const string MethodAttribute = "V-METHOD-ATTR";

    /// <summary>The <c>Execute</c> method signature does not match the required DotBoxD.Kernels entrypoint signature.</summary>
    public const string ExecuteSignature = "V-EXECUTE-SIGNATURE";

    /// <summary>A generated <c>Fn_*</c> helper does not match the required DotBoxD.Kernels function signature.</summary>
    public const string FunctionSignature = "V-FUNCTION-SIGNATURE";

    /// <summary>The generated type or a method declares generic parameters, which are not allowed.</summary>
    public const string Generic = "V-GENERIC";

    /// <summary>The operand stack underflowed, mismatched the signature, or branch heights are inconsistent.</summary>
    public const string Stack = "V-STACK";

    /// <summary>A value on the operand stack has a type that is not allowed at that position.</summary>
    public const string StackType = "V-STACK-TYPE";

    /// <summary>An argument or local operand index is out of range for the method.</summary>
    public const string Operand = "V-OPERAND";

    /// <summary>A disallowed metadata table is present (interfaces, properties, events, nested types, layout, and similar).</summary>
    public const string MetadataShape = "V-METADATA-SHAPE";

    /// <summary>The assembly carries declarative security metadata, which is not allowed.</summary>
    public const string Security = "V-SECURITY";

    private static readonly IReadOnlyList<VerifierDiagnosticReference> References =
        VerifierDiagnosticReferenceCatalog.All;

    private static readonly IReadOnlyDictionary<string, VerifierDiagnosticReference> ReferencesByCode =
        References.ToDictionary(reference => reference.Code, StringComparer.Ordinal);

    /// <summary>
    /// The complete, maintained catalog of verifier diagnostic codes. Every <c>V-*</c> code that
    /// <see cref="IGeneratedAssemblyVerifier"/> can emit has exactly one entry here.
    /// </summary>
    public static IReadOnlyList<VerifierDiagnosticReference> All => References;

    /// <summary>
    /// Looks up the documented reference for a diagnostic <paramref name="code"/>.
    /// Returns <see langword="false"/> for unknown codes; callers on the security path should treat
    /// an unknown code conservatively rather than assuming it is benign.
    /// </summary>
    public static bool TryGetReference(string code, out VerifierDiagnosticReference reference)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (ReferencesByCode.TryGetValue(code, out var match))
        {
            reference = match;
            return true;
        }

        reference = new VerifierDiagnosticReference(
            code,
            VerifierDiagnosticCategory.MalformedArtifact,
            "Unknown verifier diagnostic code with no public reference entry.",
            "A verifier code was emitted that is not catalogued in this reference, likely a newly added code.",
            "Update the public diagnostic reference to document this code; until then treat it as a rejection signal.",
            false);
        return false;
    }
}
