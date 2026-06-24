using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static partial class PluginServerFacadeModelFactory
{
    private static INamedTypeSymbol? ResolveWorldType(INamedTypeSymbol type)
    {
        foreach (var candidate in type.Interfaces)
        {
            if (HasAttribute(candidate, DotBoxDMetadataNames.DotBoxDServiceAttribute))
            {
                return candidate;
            }
        }

        return null;
    }

    private static INamedTypeSymbol? ResolveControlService(
        Compilation compilation,
        INamedTypeSymbol worldType)
    {
        var worldNamespace = worldType.ContainingNamespace.ToDisplayString();
        return compilation.GetTypeByMetadataName(worldNamespace + ".Ipc.IGamePluginControlService");
    }
}
