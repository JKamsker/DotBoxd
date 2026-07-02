using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static partial class PluginServerFacadeModelFactory
{
    private static void ValidateContextMembers(INamedTypeSymbol contextType, CancellationToken cancellationToken)
    {
        foreach (var member in ContextMembersIncludingInherited(contextType))
        {
            if (member.IsImplicitlyDeclared ||
                (!IsDeclaredOnContext(contextType, member) && member.DeclaredAccessibility == Accessibility.Private))
            {
                continue;
            }

            if (member is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
            {
                if (IsDeclaredOnContext(contextType, constructor))
                {
                    ValidateNoGeneratedContextConstructorCollision(contextType, constructor);
                }

                continue;
            }

            ValidateNoContextHostBinding(contextType, member);

            if (string.Equals(member.Name, "OnCreated", StringComparison.Ordinal))
            {
                ValidateOnCreatedMember(contextType, member, cancellationToken);
                continue;
            }

            if (GeneratedContextMemberCollides(member.Name))
            {
                throw GeneratedContextCollision(contextType, member.Name);
            }
        }
    }

    private static IEnumerable<ISymbol> ContextMembersIncludingInherited(INamedTypeSymbol contextType)
    {
        for (INamedTypeSymbol? current = contextType; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                yield return member;
            }
        }
    }

    private static void ValidateNoGeneratedContextConstructorCollision(
        INamedTypeSymbol contextType,
        IMethodSymbol constructor)
    {
        if (constructor.Parameters.Length == 1 &&
            string.Equals(
                constructor.Parameters[0].Type.ToDisplayString(),
                DotBoxDMetadataNames.HookContextType,
                StringComparison.Ordinal))
        {
            throw GeneratedContextCollision(contextType, "constructor");
        }
    }

    private static void ValidateOnCreatedMember(
        INamedTypeSymbol contextType,
        ISymbol member,
        CancellationToken cancellationToken)
    {
        if (member is IMethodSymbol method &&
            IsDeclaredOnContext(contextType, method) &&
            IsSupportedOnCreatedHook(method, cancellationToken))
        {
            return;
        }

        throw new NotSupportedException(
            $"Generated plugin server context '{contextType.ToDisplayString()}' member 'OnCreated' must be declared as partial void OnCreated(HookContext raw).");
    }

    private static bool IsSupportedOnCreatedHook(IMethodSymbol method, CancellationToken cancellationToken)
        => method.MethodKind == MethodKind.Ordinary &&
           method.DeclaredAccessibility == Accessibility.Private &&
           method is { IsStatic: false, IsGenericMethod: false, ReturnsVoid: true, Parameters.Length: 1 } &&
           method.Parameters[0].RefKind == RefKind.None &&
           string.Equals(
               method.Parameters[0].Type.ToDisplayString(),
               DotBoxDMetadataNames.HookContextType,
               StringComparison.Ordinal) &&
           HasPartialModifier(method, cancellationToken);

    private static bool HasPartialModifier(IMethodSymbol method, CancellationToken cancellationToken)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is MethodDeclarationSyntax declaration &&
                declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return true;
            }
        }

        return false;
    }

    private static bool GeneratedContextMemberCollides(string name)
        => name is "_raw" or
            "Raw" or
            "World" or
            "Messages" or
            "CancellationToken" or
            "HasCancelableDispatch" or
            "FromHookContext";

    private static NotSupportedException GeneratedContextCollision(
        INamedTypeSymbol contextType,
        string memberName)
        => new(
            $"Generated plugin server context '{contextType.ToDisplayString()}' member '{memberName}' collides with the generated context surface.");

    private static void ValidateNoContextHostBinding(INamedTypeSymbol contextType, ISymbol member)
    {
        foreach (var attribute in member.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDMetadataNames.HostBindingAttribute,
                    StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"Generated plugin server context '{contextType.ToDisplayString()}' must not declare [HostBinding] members; expose host services through [RpcService] selectors.");
            }
        }
    }

    private static bool IsDeclaredOnContext(INamedTypeSymbol contextType, ISymbol member)
        => SymbolEqualityComparer.Default.Equals(member.ContainingType, contextType);
}
