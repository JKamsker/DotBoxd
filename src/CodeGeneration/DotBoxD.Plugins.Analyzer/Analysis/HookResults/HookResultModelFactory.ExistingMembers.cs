using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookResults;

internal static partial class HookResultModelFactory
{
    // Author-declared members that must not be shadowed by generated builders. Same-arity methods are
    // conservative opt-outs because they can change overload resolution in the syntactic hook-chain lowerer.
    private static IEnumerable<HookResultExistingMember> ExistingMembers(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared)
            {
                continue;
            }

            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } method)
            {
                yield return new HookResultExistingMember(method.Name, method.Parameters.Length, BlocksAllOverloads: false);
                continue;
            }

            if (member is IPropertySymbol or IFieldSymbol or IEventSymbol)
            {
                yield return new HookResultExistingMember(member.Name, ParameterCount: -1, BlocksAllOverloads: true);
            }
        }
    }
}
