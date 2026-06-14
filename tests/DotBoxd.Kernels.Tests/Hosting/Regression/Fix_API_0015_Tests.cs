using System.ComponentModel;
using System.Reflection;
using DotBoxd.Kernels.Runtime;

namespace DotBoxd.Kernels.Tests;

// Regression coverage for API-0015: the generated-runtime facade ships without a
// public support boundary. CompiledRuntime is a verifier/compiler-owned ABI for
// generated assemblies (see VerifierTypeNames.CompiledRuntimeName,
// VerificationPolicy.BoxedValueDefaults, and BindingRegistryValidator's approved
// compiled target), yet it is exposed as an ordinary public static class with no
// marker that separates generated-code ABI from supported host-authored API.
//
// The finding's suggested fix is to "pick one support model and enforce it":
//   * [EditorBrowsable(EditorBrowsableState.Never)] + docs, OR
//   * an [Obsolete]-style do-not-call marker, OR
//   * segment it into a clearly named ABI namespace such as DotBoxd.Kernels.Runtime.Generated.
//
// These tests assert that AT LEAST ONE of those support-boundary signals exists.
// All three are expressible with types that already ship in the BCL today, so this
// file compiles against the current codebase no matter which model the fix chooses.
// It is RED right now because CompiledRuntime carries none of them.
public sealed class Fix_API_0015_Tests
{
    private static readonly Type RuntimeFacade = typeof(CompiledRuntime);

    [Fact]
    public void CompiledRuntime_declares_an_explicit_generated_abi_support_boundary()
    {
        var hasEditorBrowsableNever = HasEditorBrowsableNever(RuntimeFacade);
        var hasDoNotCallMarker = HasDoNotCallMarker(RuntimeFacade);
        var isSegmentedNamespace = IsSegmentedAbiNamespace(RuntimeFacade);

        Assert.True(
            hasEditorBrowsableNever || hasDoNotCallMarker || isSegmentedNamespace,
            "Public generated-runtime facade " + RuntimeFacade.FullName +
            " ships without any support boundary. Expected exactly one support model: " +
            "[EditorBrowsable(EditorBrowsableState.Never)], an [Obsolete] do-not-call marker, " +
            "or placement in a segmented ABI namespace such as DotBoxd.Kernels.Runtime.Generated. " +
            "Found none, so NuGet consumers can compile against the low-level ABI as if it were " +
            "supported host API.");
    }

    [Fact]
    public void CompiledRuntime_is_hidden_from_normal_host_api_discovery()
    {
        // The facade must not surface in ordinary IntelliSense/API discovery as a
        // first-class host type. Either it is browsable-hidden, or it lives in a
        // namespace whose final segment marks it as generated-code-only.
        var hidden = HasEditorBrowsableNever(RuntimeFacade) || IsSegmentedAbiNamespace(RuntimeFacade);

        Assert.True(
            hidden,
            RuntimeFacade.FullName + " is discoverable as normal host API. A generated-code ABI " +
            "facade must be hidden via [EditorBrowsable(EditorBrowsableState.Never)] or segmented " +
            "into a generated ABI namespace so it is not presented as supported host surface.");
    }

    private static bool HasEditorBrowsableNever(Type type)
    {
        var attribute = type.GetCustomAttribute<EditorBrowsableAttribute>(inherit: false);
        return attribute is not null && attribute.State == EditorBrowsableState.Never;
    }

    private static bool HasDoNotCallMarker(Type type)
        => type.GetCustomAttribute<ObsoleteAttribute>(inherit: false) is not null;

    private static bool IsSegmentedAbiNamespace(Type type)
    {
        var ns = type.Namespace;
        if (string.IsNullOrEmpty(ns) || ns == "DotBoxd.Kernels.Runtime")
        {
            return false;
        }

        // A deeper, intent-revealing namespace (e.g. DotBoxd.Kernels.Runtime.Generated) marks
        // the facade as generated-code-only rather than top-level host API.
        return ns.StartsWith("DotBoxd.Kernels.Runtime.", StringComparison.Ordinal);
    }
}
