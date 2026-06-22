using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

/// <summary>
/// Produces the namespace-qualified name an event type is recorded under in a generated package
/// manifest (<c>PluginManifest.Contract</c> and each <c>HookSubscriptionManifest.Event</c>).
/// </summary>
/// <remarks>
/// Historically the manifest recorded the <b>unqualified</b> metadata name (e.g. <c>MonsterKilledEvent</c>),
/// which is ambiguous whenever two contract types share a simple name. We now record the fully-qualified
/// name (<c>Namespace.TypeName</c>, no assembly), which matches <see cref="System.Type.FullName"/> for an
/// ordinary top-level event type so the runtime can compare against <c>typeof(TEvent).FullName</c>. The
/// runtime keeps a simple-name fallback for backward compatibility with manifests produced before this
/// change and for hand-written event adapters that report the unqualified name.
/// </remarks>
internal static class EventTypeName
{
    public static string Qualified(INamedTypeSymbol eventType)
        => eventType.ContainingNamespace.IsGlobalNamespace
            ? eventType.MetadataName
            : eventType.ContainingNamespace.ToDisplayString() + "." + eventType.MetadataName;

    public static string HookOrQualified(INamedTypeSymbol eventType)
    {
        foreach (var attribute in eventType.GetAttributes())
        {
            if (string.Equals(attribute.AttributeClass?.ToDisplayString(), DotBoxDMetadataNames.HookAttribute, StringComparison.Ordinal) &&
                attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Value is string declaredName &&
                !string.IsNullOrWhiteSpace(declaredName))
            {
                return declaredName;
            }
        }

        return Qualified(eventType);
    }
}
