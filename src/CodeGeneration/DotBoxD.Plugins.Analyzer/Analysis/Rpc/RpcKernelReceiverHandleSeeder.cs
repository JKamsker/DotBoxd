namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using Microsoft.CodeAnalysis;
using static DotBoxDRpcJsonLowerer;

internal static class RpcKernelReceiverHandleSeeder
{
    public const string ReceiverIdParameter = "__receiverId";

    public static bool TrySeed(
        DotBoxDRpcJsonLowerer lowerer,
        INamedTypeSymbol kernelType,
        RpcServerExtensionGraft? graft)
    {
        if (graft is not { InjectsReceiverId: true })
        {
            return false;
        }

        var fieldNames = new HashSet<string>(graft.ReceiverHandleFields, StringComparer.Ordinal);
        for (INamedTypeSymbol? current = kernelType; current is not null && fieldNames.Count > 0; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (!fieldNames.Contains(member.Name) ||
                    !IsVisibleReceiverMember(member, kernelType) ||
                    !CanStoreReceiver(member, graft.ReceiverType))
                {
                    continue;
                }

                lowerer.AddServiceHandleMember(member, Var(ReceiverIdParameter));
                fieldNames.Remove(member.Name);
            }
        }

        return true;
    }

    private static bool IsVisibleReceiverMember(ISymbol member, INamedTypeSymbol kernelType)
        => SymbolEqualityComparer.Default.Equals(member.ContainingType, kernelType) ||
           member.DeclaredAccessibility != Accessibility.Private;

    private static bool CanStoreReceiver(ISymbol member, INamedTypeSymbol receiverType)
        => member switch
        {
            IFieldSymbol { IsStatic: false } field => CanStoreReceiver(field.Type, receiverType),
            IPropertySymbol { IsStatic: false, GetMethod: not null, SetMethod: null } property
                => CanStoreReceiver(property.Type, receiverType),
            _ => false
        };

    private static bool CanStoreReceiver(ITypeSymbol memberType, INamedTypeSymbol receiverType)
    {
        if (SymbolEqualityComparer.Default.Equals(memberType, receiverType))
        {
            return true;
        }

        if (memberType is not INamedTypeSymbol namedMember)
        {
            return false;
        }

        foreach (var inherited in receiverType.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(namedMember, inherited))
            {
                return true;
            }
        }

        return false;
    }
}
