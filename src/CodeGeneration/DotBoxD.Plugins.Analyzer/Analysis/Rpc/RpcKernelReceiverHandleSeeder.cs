using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using static DotBoxDRpcJsonLowerer;

internal static class RpcKernelReceiverHandleSeeder
{
    public const string ReceiverIdParameter = "__receiverId";

    public static bool TrySeed(
        DotBoxDRpcJsonLowerer lowerer,
        INamedTypeSymbol kernelType,
        INamedTypeSymbol? graftType)
    {
        if (graftType is null || !HasDotBoxDServiceAttribute(graftType))
        {
            return false;
        }

        var seeded = false;
        foreach (var member in kernelType.GetMembers())
        {
            if (member is IFieldSymbol field &&
                SymbolEqualityComparer.Default.Equals(field.Type, graftType))
            {
                lowerer.AddServiceHandleLocal(field.Name, Var(ReceiverIdParameter));
                seeded = true;
            }
        }

        return seeded;
    }

    private static bool HasDotBoxDServiceAttribute(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (string.Equals(
                attribute.AttributeClass?.ToDisplayString(),
                DotBoxDMetadataNames.DotBoxDServiceAttribute,
                StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
