namespace DotBoxD.Plugins.Runtime;

/// <summary>
/// Compares the event-type name a package manifest subscribes to (<c>HookSubscriptionManifest.Event</c> /
/// the <c>IEventKernel&lt;TEvent&gt;</c> contract payload) against the name a runtime event adapter or a
/// <c>typeof(TEvent)</c> reports.
/// </summary>
/// <remarks>
/// The analyzer now emits the <b>fully-qualified</b> name (<c>Namespace.TypeName</c>) into generated
/// manifests so two contract types that share a simple name stay distinct. Existing manifests produced
/// before that change — and hand-written <see cref="IPluginEventAdapter{TEvent}"/> implementations that
/// return only <c>typeof(TEvent).Name</c> — still carry the unqualified simple name. To stay backward
/// compatible while keeping the new disambiguation, two names match when they are ordinally equal OR when
/// one is a namespace-qualified form whose final segment equals the other's simple name. Comparing the
/// simple-name tail is unambiguous in practice because the producer always pairs a manifest's contract and
/// subscription event from the same symbol, so only the qualified-vs-simple seam between manifest and
/// adapter is bridged here, never two unrelated qualified names.
/// </remarks>
internal static class EventNameMatch
{
    public static bool Matches(string? left, string? right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return false;
        }

        return string.Equals(SimpleName(left!), SimpleName(right!), StringComparison.Ordinal);
    }

    private static string SimpleName(string name)
    {
        var lastDot = name.LastIndexOf('.');
        return lastDot >= 0 && lastDot < name.Length - 1
            ? name[(lastDot + 1)..]
            : name;
    }
}
