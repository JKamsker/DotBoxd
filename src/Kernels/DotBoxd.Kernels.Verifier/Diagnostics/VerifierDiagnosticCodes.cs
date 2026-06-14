namespace DotBoxd.Kernels.Verifier;

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
    /// Either a corrupt/tampered artifact or a non-DotBoxd.Kernels producer.
    /// </summary>
    MalformedArtifact,

    /// <summary>
    /// The generated assembly references types, assemblies, members, or IL that the sandbox
    /// policy forbids. Indicates disallowed capabilities reaching the verifier.
    /// </summary>
    ForbiddenCapability,

    /// <summary>
    /// The generated code does not match the shape the DotBoxd.Kernels compiler is expected to emit
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
/// Whether well-formed output from the canonical DotBoxd.Kernels compiler can legitimately produce this code.
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

    /// <summary>Portable Executable container is malformed or not a valid DotBoxd.Kernels DLL.</summary>
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

    /// <summary>An array allocation uses an element type other than the allowed DotBoxd.Kernels value type.</summary>
    public const string Array = "V-ARRAY";

    /// <summary>The compiled <c>Execute</c>/function shape does not match the required DotBoxd.Kernels dispatch shape.</summary>
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

    /// <summary>The <c>Execute</c> method signature does not match the required DotBoxd.Kernels entrypoint signature.</summary>
    public const string ExecuteSignature = "V-EXECUTE-SIGNATURE";

    /// <summary>A generated <c>Fn_*</c> helper does not match the required DotBoxd.Kernels function signature.</summary>
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
    [
        new(ManifestHash, VerifierDiagnosticCategory.ArtifactIntegrity,
            "Assembly hash is missing from the manifest or does not match the artifact bytes.",
            "The artifact was altered after the manifest was produced, or the wrong manifest is paired with the artifact.",
            "Treat as a tampering/cache-poisoning signal. Re-fetch the artifact and manifest from a trusted source and re-verify.",
            false),
        new(ManifestIdentity, VerifierDiagnosticCategory.HostVersionMismatch,
            "Manifest identity does not match the expected verification context, or no expected identity was supplied.",
            "Compiler/type-system/runtime/verifier versions differ from the host, or the artifact came from a different release.",
            "Confirm the artifact was produced by a compatible toolchain version, then re-verify with the matching expected manifest identity.",
            false),
        new(PeFormat, VerifierDiagnosticCategory.MalformedArtifact,
            "Portable Executable container is malformed or is not a valid DotBoxd.Kernels DLL.",
            "Corrupt, truncated, or tampered bytes, or output from a non-DotBoxd.Kernels producer.",
            "Re-produce the artifact from source and re-verify; reject the current bytes.",
            false),
        new(PeMetadata, VerifierDiagnosticCategory.MalformedArtifact,
            "Assembly has no CLR metadata or CLR header.",
            "The bytes are not a managed assembly, or metadata was stripped/corrupted.",
            "Reject the artifact and re-produce it from a trusted compiler.",
            false),
        new(PeMixed, VerifierDiagnosticCategory.MalformedArtifact,
            "Assembly is mixed-mode or contains native code.",
            "A non-DotBoxd.Kernels or tampered producer emitted native code.",
            "Reject the artifact; only IL-only managed assemblies are accepted.",
            false),
        new(PeEntrypoint, VerifierDiagnosticCategory.MalformedArtifact,
            "Generated artifact defines an entrypoint, which is not allowed.",
            "A non-DotBoxd.Kernels or tampered producer marked an entrypoint.",
            "Reject the artifact; generated modules must not declare an entrypoint.",
            false),
        new(PeNative, VerifierDiagnosticCategory.MalformedArtifact,
            "Assembly contains native PE or CLR header directories.",
            "Native export/import/TLS or native CLR header directories were present.",
            "Reject the artifact; native directories are never emitted by the canonical compiler.",
            false),
        new(PeSection, VerifierDiagnosticCategory.MalformedArtifact,
            "Assembly contains a disallowed PE section or unsafe section permissions.",
            "Sections beyond .text/.rsrc/.reloc, or writable+executable sections, were present.",
            "Reject the artifact; only the canonical read-only/executable section layout is allowed.",
            false),
        new(IlFormat, VerifierDiagnosticCategory.MalformedArtifact,
            "A method body contained malformed or unreadable IL.",
            "Corrupt or tampered method-body bytes.",
            "Reject the artifact and re-produce it from source.",
            false),
        new(AssemblyReference, VerifierDiagnosticCategory.ForbiddenCapability,
            "An assembly reference is not on the policy allow-list.",
            "The generated code referenced an assembly the sandbox policy does not permit.",
            "Remove the disallowed dependency from the source program, or widen the policy only if intentionally trusted.",
            false),
        new(TypeForbidden, VerifierDiagnosticCategory.ForbiddenCapability,
            "A type reference matches a policy-forbidden prefix.",
            "The source program used a type family explicitly denied by policy (for example IO or reflection types).",
            "Remove the forbidden type usage from the source program.",
            false),
        new(TypeReference, VerifierDiagnosticCategory.ForbiddenCapability,
            "A type reference is not on the policy allow-list.",
            "The source program referenced a type outside the allowed surface.",
            "Restrict the source program to allowed types, or extend the policy only for trusted types.",
            false),
        new(Member, VerifierDiagnosticCategory.ForbiddenCapability,
            "A member reference or local call target is not allowed by policy.",
            "The source program called a method/field outside the allowed surface, or a non-static local target.",
            "Restrict the source program to allowed members; local calls must target static helpers.",
            false),
        new(CustomAttribute, VerifierDiagnosticCategory.ForbiddenCapability,
            "The generated assembly carries a custom attribute, which is not allowed.",
            "A non-DotBoxd.Kernels producer or source program attached attributes the verifier rejects.",
            "Remove custom attributes from the generated surface.",
            false),
        new(Resource, VerifierDiagnosticCategory.ForbiddenCapability,
            "The assembly embeds resources, which are not allowed.",
            "Embedded manifest resources were present in the artifact.",
            "Reject the artifact; generated modules must not embed resources.",
            false),
        new(PInvoke, VerifierDiagnosticCategory.ForbiddenCapability,
            "The assembly carries P/Invoke metadata, which is not allowed.",
            "ImplMap (P/Invoke) metadata was present.",
            "Reject the artifact; native interop is never allowed.",
            false),
        new(OpCode, VerifierDiagnosticCategory.ForbiddenCapability,
            "An IL opcode used in a method body is not on the allow-list or is explicitly forbidden.",
            "The source program or a tampered body used a disallowed opcode (for example calli, ldftn, ldtoken).",
            "Restrict the source program to the supported instruction set.",
            false),
        new(ControlFlow, VerifierDiagnosticCategory.MalformedArtifact,
            "A branch targets an offset that is not a valid instruction boundary.",
            "Corrupt or tampered control flow in a method body.",
            "Reject the artifact and re-produce it from source.",
            false),
        new(Exception, VerifierDiagnosticCategory.UnsupportedGeneratedShape,
            "A method body declares exception handlers, which are not allowed.",
            "The generated body used try/catch/finally regions.",
            "Reject the artifact; the canonical compiler does not emit exception regions.",
            false),
        new(Array, VerifierDiagnosticCategory.ForbiddenCapability,
            "An array allocation uses an element type other than the allowed DotBoxd.Kernels value type.",
            "The source program allocated arrays of a disallowed element type.",
            "Only allocate arrays of the supported DotBoxd.Kernels value type.",
            false),
        new(CompiledShape, VerifierDiagnosticCategory.UnsupportedGeneratedShape,
            "The compiled Execute/function shape does not match the required DotBoxd.Kernels dispatch shape.",
            "The generated method does not validate input and dispatch in the canonical metered shape.",
            "Re-produce with the canonical compiler; do not hand-edit generated dispatch bodies.",
            false),
        new(PublicSurface, VerifierDiagnosticCategory.UnsupportedGeneratedShape,
            "The public surface is wrong: extra types/methods, a missing Execute, or bad visibility/name.",
            "The generated assembly exposes more than the single canonical type with one public static Execute.",
            "Re-produce with the canonical compiler so the public surface matches exactly.",
            false),
        new(ModuleSurface, VerifierDiagnosticCategory.UnsupportedGeneratedShape,
            "Module-level (<Module>) methods or fields are present, which is not allowed.",
            "A non-canonical producer added global members.",
            "Reject the artifact; the canonical compiler emits no module-level members.",
            false),
        new(TypeShape, VerifierDiagnosticCategory.UnsupportedGeneratedShape,
            "The generated type is not a static (abstract+sealed) non-interface type.",
            "The generated type was declared as an instance type or interface.",
            "Re-produce with the canonical compiler so the generated type is static and sealed.",
            false),
        new(Field, VerifierDiagnosticCategory.UnsupportedGeneratedShape,
            "The generated type declares fields, which is not allowed.",
            "A non-canonical producer added fields to the generated type.",
            "Reject the artifact; generated types must be stateless.",
            false),
        new(FieldStatic, VerifierDiagnosticCategory.UnsupportedGeneratedShape,
            "The generated type declares a mutable static field, which is not allowed.",
            "A non-canonical producer added shared mutable state.",
            "Reject the artifact; static fields must be init-only or literal if present at all.",
            false),
        new(MethodSpecial, VerifierDiagnosticCategory.UnsupportedGeneratedShape,
            "A disallowed special method (such as .cctor or Finalize) is present.",
            "A non-canonical producer added a static constructor or finalizer.",
            "Reject the artifact; special methods are never emitted by the canonical compiler.",
            false),
        new(MethodPInvoke, VerifierDiagnosticCategory.ForbiddenCapability,
            "A method carries P/Invoke attributes.",
            "A method was marked for native interop.",
            "Reject the artifact; native interop is not allowed.",
            false),
        new(MethodBody, VerifierDiagnosticCategory.MalformedArtifact,
            "A generated executable method is missing its method body.",
            "An Execute or Fn_* method has no IL body.",
            "Reject the artifact and re-produce it from source.",
            false),
        new(MethodName, VerifierDiagnosticCategory.UnsupportedGeneratedShape,
            "A method name is not an expected generated method name.",
            "A non-canonical producer added unexpected methods.",
            "Re-produce with the canonical compiler so only expected method names are present.",
            false),
        new(MethodAttribute, VerifierDiagnosticCategory.UnsupportedGeneratedShape,
            "A method uses unsupported attributes (virtual/abstract/synchronized/native/internal-call/non-static).",
            "A non-canonical producer emitted methods with disallowed implementation or method attributes.",
            "Re-produce with the canonical compiler so methods are concrete, static, and managed.",
            false),
        new(ExecuteSignature, VerifierDiagnosticCategory.UnsupportedGeneratedShape,
            "The Execute method signature does not match the required DotBoxd.Kernels entrypoint signature.",
            "Execute does not match SandboxValue Execute(SandboxContext, SandboxValue).",
            "Re-produce with the canonical compiler so Execute matches the required signature.",
            false),
        new(FunctionSignature, VerifierDiagnosticCategory.UnsupportedGeneratedShape,
            "A generated Fn_* helper does not match the required DotBoxd.Kernels function signature.",
            "A Fn_* helper does not match SandboxValue Fn_*(SandboxContext, SandboxValue...).",
            "Re-produce with the canonical compiler so helper signatures match.",
            false),
        new(Generic, VerifierDiagnosticCategory.UnsupportedGeneratedShape,
            "The generated type or a method declares generic parameters, which are not allowed.",
            "A non-canonical producer emitted generic parameters.",
            "Reject the artifact; the canonical compiler emits no generics.",
            false),
        new(Stack, VerifierDiagnosticCategory.MalformedArtifact,
            "The operand stack underflowed, mismatched the signature, or branch heights are inconsistent.",
            "Corrupt, tampered, or non-canonical IL with an inconsistent operand stack.",
            "Reject the artifact and re-produce it from source.",
            false),
        new(StackType, VerifierDiagnosticCategory.MalformedArtifact,
            "A value on the operand stack has a type that is not allowed at that position.",
            "Non-canonical or tampered IL placed a disallowed type on the stack.",
            "Reject the artifact and re-produce it from source.",
            false),
        new(Operand, VerifierDiagnosticCategory.MalformedArtifact,
            "An argument or local operand index is out of range for the method.",
            "Tampered or non-canonical IL referenced an argument/local that does not exist.",
            "Reject the artifact and re-produce it from source.",
            false),
        new(MetadataShape, VerifierDiagnosticCategory.UnsupportedGeneratedShape,
            "A disallowed metadata table is present (interfaces, properties, events, nested types, layout, and similar).",
            "A non-canonical producer emitted metadata the generated shape never contains.",
            "Reject the artifact; re-produce it with the canonical compiler so only the supported metadata tables are present.",
            false),
        new(Security, VerifierDiagnosticCategory.ForbiddenCapability,
            "The assembly carries declarative security metadata, which is not allowed.",
            "DeclSecurity metadata was present in the artifact.",
            "Reject the artifact; declarative security is never emitted by the canonical compiler.",
            false),
    ];

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
