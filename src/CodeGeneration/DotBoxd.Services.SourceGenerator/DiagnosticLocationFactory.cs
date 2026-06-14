using Microsoft.CodeAnalysis;

namespace DotBoxd.Services.SourceGenerator;

internal static class DiagnosticLocationFactory
{
    public static DiagnosticLocation FromSymbol(ISymbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            if (location.IsInSource)
            {
                return DiagnosticLocation.FromLocation(location);
            }
        }

        return default;
    }
}
