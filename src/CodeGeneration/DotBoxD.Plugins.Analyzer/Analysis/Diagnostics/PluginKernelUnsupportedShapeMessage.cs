using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class PluginKernelUnsupportedShapeMessage
{
    public static string EventProperties(INamedTypeSymbol eventType)
    {
        foreach (var property in PluginEventPropertyReader.Read(eventType))
        {
            if (PolymorphicHandleMetadataReader.TryResolve(property.Type, out _) ||
                SandboxTypeSourceEmitter.TryEmit(property.Type) is not null)
            {
                continue;
            }

            return $"Kernel event property '{property.Name}' type '{property.Type.ToDisplayString()}' is " +
                   "unsupported; event properties must use marshaller-supported scalar, Guid, enum, " +
                   "list/array, map, or DTO record types.";
        }

        return "Kernel event properties contain an unsupported type.";
    }
}
