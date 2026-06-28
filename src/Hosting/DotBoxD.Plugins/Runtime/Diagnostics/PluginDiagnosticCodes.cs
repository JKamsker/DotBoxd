using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Runtime.Lifecycle;

namespace DotBoxD.Plugins.Runtime.Diagnostics;

/// <summary>
/// The runtime phase that emits a plugin <c>DBXK*</c> diagnostic, so a host can tell a
/// static-package-upload rejection from a prepared-plan mismatch or a live runtime check.
/// </summary>
public enum PluginDiagnosticPhase
{
    /// <summary>
    /// Static plugin-package validation when a package is uploaded or generated, before it is
    /// prepared against a DotBoxD.Kernels plan. Emitted from <see cref="PluginPackageValidator"/>.
    /// </summary>
    PackageValidation,

    /// <summary>
    /// Prepared-package validation after the package has been validated against the prepared
    /// DotBoxD.Kernels plan and registered event adapters. Emitted from <see cref="PluginPreparedPackageValidator"/>.
    /// </summary>
    PreparedPackageValidation,

    /// <summary>
    /// Direct/hook kernel entrypoint validation when a kernel is wired to an event adapter at
    /// runtime, or when a hook pipeline is registered.
    /// </summary>
    RuntimeEntrypoint,

    /// <summary>
    /// Live-setting type, range, and value validation when manifest settings are validated or a
    /// live setting is updated. Emitted from <see cref="LiveSettingTypeConverter"/>.
    /// </summary>
    LiveSetting,
}

/// <summary>
/// Identifies who must act on a runtime plugin diagnostic. Distinguishes a plugin-authoring
/// mistake (fix the plugin source/manifest and re-upload) from a host/operator configuration
/// problem (fix the live setting value or registration the host supplied).
/// </summary>
public enum PluginDiagnosticAudience
{
    /// <summary>The plugin author must fix the plugin source or manifest and re-upload the package.</summary>
    PluginAuthor,

    /// <summary>The host or operator must fix the value or registration they supplied at runtime.</summary>
    HostOperator,
}

/// <summary>
/// Public reference entry for a single runtime plugin diagnostic
/// <see cref="SandboxDiagnostic.Code"/> emitted by the <c>DotBoxD.Plugins</c> package.
/// </summary>
/// <param name="Code">The stable <c>DBXK*</c> diagnostic code.</param>
/// <param name="Phase">The runtime phase that emits the code.</param>
/// <param name="Audience">Who must act on the diagnostic.</param>
/// <param name="Meaning">A short human-readable description of the rule that was violated.</param>
/// <param name="LikelyCause">The most common reason this code is emitted.</param>
/// <param name="Remediation">Guidance for the plugin author or host operator investigating the code.</param>
public sealed record PluginDiagnosticReference(
    string Code,
    PluginDiagnosticPhase Phase,
    PluginDiagnosticAudience Audience,
    string Meaning,
    string LikelyCause,
    string Remediation);

/// <summary>
/// Public, maintained reference for the runtime <c>DBXK*</c> diagnostic codes emitted by the
/// <c>DotBoxD.Plugins</c> package during plugin-package install, prepared-package validation,
/// runtime kernel entrypoint checks, and live-setting validation.
/// </summary>
/// <remarks>
/// This is the runtime counterpart to the <c>DotBoxD.Plugins.Analyzer</c> analyzer-local
/// <c>DBXK</c> reference: the analyzer documents compile-time SDK diagnostics, while this catalog
/// documents the diagnostics a host or upload UI sees when an uploaded or generated package is
/// rejected. Consumers can use <see cref="All"/> / <see cref="TryGetReference"/> to map an
/// <c>DBXK*</c> code to its documented meaning, emitting phase, responsible audience, likely cause,
/// and remediation instead of surfacing an opaque code.
/// </remarks>
public static class PluginDiagnosticCodes
{
    private static readonly IReadOnlyList<PluginDiagnosticReference> References =
    [
        new("DBXK010", PluginDiagnosticPhase.PackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "The plugin manifest does not declare a plugin id.",
            "The manifest's plugin id is empty or whitespace.",
            "Set a non-empty plugin id on the manifest and re-upload the package."),
        new("DBXK011", PluginDiagnosticPhase.PackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "The manifest plugin id does not match the module id.",
            "The manifest and the compiled module disagree on the plugin identity.",
            "Align the manifest plugin id with the module id so identity is consistent, then re-upload."),
        new("DBXK012", PluginDiagnosticPhase.PackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "The module metadata does not bind to the manifest plugin id.",
            "The module is missing its plugin-id metadata, or it differs from the manifest plugin id.",
            "Regenerate the package so module metadata binds to the same plugin id as the manifest."),
        new("DBXK013", PluginDiagnosticPhase.PackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "The module kernel metadata is missing or a hook subscription targets a different kernel.",
            "The module lacks kernel metadata, or a subscription's kernel does not match the module kernel.",
            "Bind the module metadata to the manifest kernel and ensure every subscription targets that kernel."),
        new("DBXK014", PluginDiagnosticPhase.PreparedPackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "The manifest contract is not a valid IEventKernel<TEvent> contract or its event does not match a subscription.",
            "The contract string is malformed, or the contract event differs from a subscription event.",
            "Declare the contract as IEventKernel<TEvent> and make the contract event match the subscribed event."),
        new("DBXK020", PluginDiagnosticPhase.LiveSetting, PluginDiagnosticAudience.PluginAuthor,
            "A live setting uses an unsupported type, or its default value is not valid for that type.",
            "The live setting type is outside bool/int/long/double/string, or the default cannot be coerced.",
            "Use a supported live setting type (bool, int, long, double, string) with a compatible default value."),
        new("DBXK021", PluginDiagnosticPhase.PackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "A live setting name is declared more than once.",
            "Two live settings share the same name in the manifest.",
            "Give each live setting a unique name and re-upload the package."),
        new("DBXK022", PluginDiagnosticPhase.LiveSetting, PluginDiagnosticAudience.PluginAuthor,
            "A live setting declares a min/max range on a non-numeric type.",
            "A range was attached to a bool or string live setting.",
            "Remove the range, or change the live setting to a numeric type (int, long, double)."),
        new("DBXK023", PluginDiagnosticPhase.LiveSetting, PluginDiagnosticAudience.HostOperator,
            "A live setting value is outside its allowed range.",
            "A supplied or updated live setting value is below the minimum or above the maximum.",
            "Supply a live setting value within the declared minimum and maximum bounds."),
        new("DBXK024", PluginDiagnosticPhase.LiveSetting, PluginDiagnosticAudience.PluginAuthor,
            "A live setting declares a minimum greater than its maximum.",
            "The manifest range bounds are inverted.",
            "Correct the range so the minimum is less than or equal to the maximum."),
        new("DBXK030", PluginDiagnosticPhase.PackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "The manifest declares no hook subscriptions.",
            "A plugin must subscribe to at least one event but the manifest has none.",
            "Declare at least one hook subscription with an event and kernel, then re-upload."),
        new("DBXK031", PluginDiagnosticPhase.PackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "A hook subscription is missing its event or kernel, or the kernel is not subscribed to the wired event.",
            "A subscription has an empty event/kernel, or a runtime kernel is wired to an event it never subscribed to.",
            "Provide both event and kernel for every subscription and only wire kernels to events they subscribe to."),
        new("DBXK032", PluginDiagnosticPhase.PackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "A required kernel entrypoint is missing or is not public.",
            "A ShouldHandle/Handle entrypoint id is empty, or no matching public entrypoint function exists.",
            "Expose the named entrypoints as public module entrypoints and reference them from the manifest."),
        new("DBXK033", PluginDiagnosticPhase.PreparedPackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "A kernel entrypoint signature does not match the hook event and live settings.",
            "ShouldHandle must return Bool and Handle must return Unit, with exact event-adapter and live-setting parameters.",
            "Match each entrypoint's return type and parameter shape to the hook event plus declared live settings."),
        new("DBXK034", PluginDiagnosticPhase.PreparedPackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "Kernel entrypoints disagree on parameter shape, or a hook pipeline is already registered with a different adapter.",
            "ShouldHandle and Handle declare different parameters, or two registrations use conflicting adapters for one event.",
            "Give both entrypoints the same parameter shape and register one adapter per event type."),
        new("DBXK035", PluginDiagnosticPhase.PreparedPackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "A kernel entrypoint does not declare the manifest live settings as trailing parameters.",
            "Live setting parameters are missing, misnamed, mistyped, or out of order at the end of the entrypoint.",
            "Append each live setting as a trailing parameter with the exact declared name and type."),
        new("DBXK036", PluginDiagnosticPhase.RuntimeEntrypoint, PluginDiagnosticAudience.HostOperator,
            "An event value writer reports a value count that does not match the registered adapter parameters.",
            "The host-provided IPluginEventValueWriter<TEvent> returns a different EventValueCount than its Parameters count.",
            "Make EventValueCount match the adapter Parameters count so reusable event-value buffers can be built safely."),
        new("DBXK040", PluginDiagnosticPhase.PackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "The manifest declares an unsupported effect, or declares no verified effects at all.",
            "An effect name is not a known SandboxEffect, or the manifest effect set is empty.",
            "Declare only supported SandboxEffect values and ensure at least one verified effect is present."),
        new("DBXK041", PluginDiagnosticPhase.PreparedPackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "The manifest effects do not match the verified entrypoint effects.",
            "The declared effect set differs from the effects the prepared plan computed for the entrypoints.",
            "Declare exactly the effects the entrypoints require so the manifest matches the verified plan."),
        new("DBXK042", PluginDiagnosticPhase.PackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "The manifest declares an unsupported execution mode.",
            "The manifest execution mode is not a defined mode value.",
            "Set the manifest execution mode to a supported value and re-upload the package."),
        new("DBXK043", PluginDiagnosticPhase.PreparedPackageValidation, PluginDiagnosticAudience.HostOperator,
            "The plugin requires async runtime capability but the install policy does not grant it.",
            "The verified entrypoint references an async binding and the policy lacks dotboxd.runtime.async.",
            "Call AllowRuntimeAsync() on the sandbox policy, or remove the async binding from the plugin."),
        new("DBXK044", PluginDiagnosticPhase.PreparedPackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "The manifest required capabilities do not match the verified entrypoint capabilities.",
            "The requiredCapabilities list was stripped, stale, duplicated, or self-asserted capabilities not present in the verified plan.",
            "Regenerate the package so requiredCapabilities is derived from the verified kernel source."),
        new("DBXK046", PluginDiagnosticPhase.PackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "A hook subscription declares an indexed predicate with an unsupported comparison operator.",
            "The manifest's indexed predicate operator is not a defined IndexPredicateOperator value (manifest hand-edited or generated by an incompatible tool).",
            "Regenerate the package so indexed predicate metadata is derived from the verified .Where(...) chain."),
        new("DBXK047", PluginDiagnosticPhase.PackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "A hook subscription declares an indexed predicate with an unsupported value type.",
            "The manifest's indexed predicate valueType is not one of bool/int/long/double/string.",
            "Regenerate the package so indexed predicate metadata is derived from the verified .Where(...) chain."),
        new("DBXK048", PluginDiagnosticPhase.PackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "A hook subscription claims full index coverage but declares no indexed predicates.",
            "indexCoversPredicate is true while indexedPredicates is empty, which would let a host skip the verified IR with no index to check.",
            "Regenerate the package; full coverage requires at least one indexed predicate."),
        new("DBXK049", PluginDiagnosticPhase.PackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "An indexed predicate value does not match its declared value type.",
            "A programmatically-built manifest boxed a value whose runtime type differs from valueType (e.g. valueType 'int' with a long value).",
            "Regenerate the package so each indexed predicate value matches its declared value type."),
        new("DBXK050", PluginDiagnosticPhase.PackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "A manifest text value is empty, contains control characters, or looks like a forbidden CLR/IL descriptor.",
            "A manifest identifier is blank, has control characters, or resembles a forbidden CLR or IL descriptor.",
            "Use non-empty manifest text without control characters or CLR/IL descriptor-like content."),
        new("DBXK060", PluginDiagnosticPhase.RuntimeEntrypoint, PluginDiagnosticAudience.HostOperator,
            "A plugin id is already owned by a different session and cannot be replaced.",
            "Two connections tried to install a kernel with the same plugin id; ids are owned by the installing session.",
            "Use a distinct plugin id per owner, or uninstall the existing kernel from its owning session first."),
        new("DBXK061", PluginDiagnosticPhase.RuntimeEntrypoint, PluginDiagnosticAudience.HostOperator,
            "A session attempted to update settings for a plugin id it does not own.",
            "An UpdateSettings call referenced a plugin id installed by a different session.",
            "Only update live settings for kernels installed by the same session that owns them."),
        new("DBXK062", PluginDiagnosticPhase.RuntimeEntrypoint, PluginDiagnosticAudience.PluginAuthor,
            "An Run(lambda) hook terminal was not lowered to verified IR and cannot run as host code.",
            "Run(lambda) was invoked at runtime without the DotBoxD.Kernels analyzer lowering it (analyzer not referenced, or an unsupported construct in the chain).",
            "Reference DotBoxD.Plugins.Analyzer so Run chains are lowered, or bind a kernel class with Use/Register instead."),
        new("DBXK063", PluginDiagnosticPhase.RuntimeEntrypoint, PluginDiagnosticAudience.HostOperator,
            "A generated hook-chain package was wired into a pipeline that has no installer.",
            "UseGeneratedChain was called on a HookPipeline created without a PluginServer-backed installer.",
            "Create the hook pipeline from PluginServer (server.Hooks) so generated chains can be installed."),
        new("DBXK064", PluginDiagnosticPhase.PreparedPackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "A subscription pipeline is already registered with a different adapter.",
            "Two registrations use conflicting adapters for one fire-and-forget event subscription.",
            "Register one adapter per event type before adding subscription handlers."),
        new("DBXK065", PluginDiagnosticPhase.RuntimeEntrypoint, PluginDiagnosticAudience.HostOperator,
            "A generated subscription-chain package was wired into a pipeline that has no installer.",
            "UseGeneratedChain was called on a SubscriptionPipeline created without a PluginServer-backed installer.",
            "Create the subscription pipeline from PluginServer (server.Subscriptions) so generated chains can be installed."),
        new("DBXK066", PluginDiagnosticPhase.RuntimeEntrypoint, PluginDiagnosticAudience.HostOperator,
            "A result hook was fired with a result type that does not match the hook context contract.",
            "HookRegistry.FireAsync<TContext, TResult> was called with a TResult different from TContext's [Hook] result type.",
            "Call FireAsync with the result type declared by the hook context's HookAttribute."),
        new("DBXK067", PluginDiagnosticPhase.RuntimeEntrypoint, PluginDiagnosticAudience.HostOperator,
            "A hook or subscription pipeline is already registered with a different context factory.",
            "Two registrations target the same event/context pair but provide different context factory delegates.",
            "Use one stable context factory per event/context pair, or choose a distinct context type."),
        new("DBXK070", PluginDiagnosticPhase.PackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "A server extension package does not declare an rpcEntrypoint.",
            "A package was installed via InstallServerExtensionAsync but its manifest has no rpcEntrypoint naming the batch entrypoint.",
            "Author the kernel with [ServerExtension] so the generator emits an rpcEntrypoint, or set it on the manifest."),
        new("DBXK071", PluginDiagnosticPhase.PackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "A server extension's rpcEntrypoint does not resolve to a public entrypoint function.",
            "The manifest's rpcEntrypoint names a function that is missing from the module or is not a public entrypoint.",
            "Ensure the batch method is a public entrypoint and matches the manifest's rpcEntrypoint name."),
        new("DBXK072", PluginDiagnosticPhase.PreparedPackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "A server extension entrypoint returns an unsupported type.",
            "The verified RPC entrypoint's return type is not a supported sandbox type (scalar, list, or record).",
            "Return a supported scalar, a List of one, or a DTO/record built from supported field types."),
        new("DBXK073", PluginDiagnosticPhase.PackageValidation, PluginDiagnosticAudience.PluginAuthor,
            "A package manifest mixes hook and RPC entrypoint shapes.",
            "RPC packages are request/response entrypoints, hook packages are event subscriptions, and a single manifest cannot be both.",
            "Remove subscriptions from the RPC manifest, or package the event hook kernel separately without rpcEntrypoint."),
    ];

    private static readonly IReadOnlyDictionary<string, PluginDiagnosticReference> ReferencesByCode =
        References.ToDictionary(reference => reference.Code, StringComparer.Ordinal);

    /// <summary>
    /// The complete, maintained catalog of runtime plugin diagnostic codes. Every <c>DBXK*</c> code
    /// that the <c>DotBoxD.Plugins</c> runtime can emit has exactly one entry here.
    /// </summary>
    public static IReadOnlyList<PluginDiagnosticReference> All => References;

    /// <summary>
    /// Looks up the documented reference for a runtime plugin diagnostic <paramref name="code"/>.
    /// Returns <see langword="false"/> for unknown codes; a host should treat an unknown code as a
    /// rejection signal rather than assuming the package is safe.
    /// </summary>
    public static bool TryGetReference(string code, out PluginDiagnosticReference reference)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (ReferencesByCode.TryGetValue(code, out var match))
        {
            reference = match;
            return true;
        }

        reference = new PluginDiagnosticReference(
            code,
            PluginDiagnosticPhase.PackageValidation,
            PluginDiagnosticAudience.PluginAuthor,
            "Unknown runtime plugin diagnostic code with no public reference entry.",
            "A DBXK code was emitted that is not catalogued in this reference, likely a newly added code.",
            "Update the public plugin diagnostics reference to document this code; until then treat it as a rejection signal.");
        return false;
    }
}
