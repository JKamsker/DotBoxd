using Microsoft.CodeAnalysis;
using static DotBoxD.Plugins.Analyzer.Analysis.PluginServer.PluginServerFacadeNameFormatter;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static partial class PluginServerFacadeModelFactory
{
    private static PluginServerForwardedMethod[] ResolveMethods(
        INamedTypeSymbol controlType,
        Dictionary<string, ServiceWrapperBuilder> serviceWrappers,
        CancellationToken cancellationToken)
    {
        var methods = new List<PluginServerForwardedMethod>();
        var seenMethods = new Dictionary<string, PluginServerForwardedMethod>(StringComparer.Ordinal);
        foreach (var member in MembersIncludingInherited(controlType))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member.ContainingType is not null &&
                IsControlPlaneMember(member.ContainingType))
            {
                continue;
            }

            if (member is IEventSymbol eventSymbol)
            {
                throw new NotSupportedException(
                    $"Generated plugin server member '{eventSymbol.ToDisplayString()}' is an event; events are not supported on generated plugin server facades.");
            }

            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } method)
            {
                ValidateForwardedMethod(method);
                var (returnWrapperName, returnWrapperKind) = ResolveReturnWrapper(
                    method.ReturnType,
                    serviceWrappers,
                    cancellationToken);
                var forwarded = new PluginServerForwardedMethod(
                    method.Name,
                    TypeName(method.ContainingType),
                    TypeName(method.ReturnType),
                    PluginServerFlowAttributeSource.ReturnAttributes(method),
                    PluginServerXmlDocumentation.FromSymbol(
                        method,
                        "Forwards " + method.Name + " to the remote domain service.",
                        cancellationToken),
                    returnWrapperName,
                    returnWrapperKind,
                    new EquatableArray<PluginServerParameter>(ResolveParameters(method)));
                var signature = MethodSignatureKey(method);
                if (seenMethods.TryGetValue(signature, out var existing))
                {
                    if (!string.Equals(existing.ReturnType, forwarded.ReturnType, StringComparison.Ordinal))
                    {
                        throw new NotSupportedException(
                            $"Generated plugin server member '{method.ToDisplayString()}' has an inherited signature collision with a different return type.");
                    }

                    if (!existing.ReturnAttributes.Equals(forwarded.ReturnAttributes))
                    {
                        throw new NotSupportedException(
                            $"Generated plugin server member '{method.ToDisplayString()}' has an inherited signature collision with different return-flow attributes.");
                    }

                    continue;
                }

                seenMethods.Add(signature, forwarded);
                methods.Add(forwarded);
            }
        }

        return methods.ToArray();
    }

    private static void ValidateForwardedMethod(IMethodSymbol method)
    {
        if (method.DeclaredAccessibility != Accessibility.Public)
        {
            throw new NotSupportedException(
                $"Generated plugin server member '{method.ToDisplayString()}' is a non-public interface method '{method.Name}'; generated plugin server facades may forward public members only.");
        }

        if (method.IsStatic)
        {
            throw new NotSupportedException(
                $"Generated plugin server member '{method.ToDisplayString()}' must be an instance method.");
        }

        if (method.IsGenericMethod)
        {
            throw new NotSupportedException(
                $"Generated plugin server member '{method.ToDisplayString()}' must not be generic.");
        }

        if (method.RefKind != RefKind.None)
        {
            throw new NotSupportedException(
                $"Generated plugin server member '{method.ToDisplayString()}' must not declare ref returns.");
        }

        foreach (var parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
            {
                throw new NotSupportedException(
                    $"Generated plugin server member '{method.ToDisplayString()}' must not declare ref, out, or in parameters.");
            }
        }
    }

    private static string MethodSignatureKey(IMethodSymbol method)
        => method.Name + "(" + string.Join(
            ",",
            method.Parameters.Select(static parameter => TypeName(parameter.Type))) + ")";
}
