using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class DotBoxDWellKnownTaskTypes
{
    public static bool IsTaskLike(
        ITypeSymbol type,
        Compilation compilation,
        out INamedTypeSymbol? taskLike)
    {
        if (type is INamedTypeSymbol named &&
            (IsDefinition(named, compilation, "System.Threading.Tasks.Task") ||
             IsDefinition(named, compilation, "System.Threading.Tasks.Task`1") ||
             IsDefinition(named, compilation, "System.Threading.Tasks.ValueTask") ||
             IsDefinition(named, compilation, "System.Threading.Tasks.ValueTask`1")))
        {
            taskLike = named;
            return true;
        }

        taskLike = null;
        return false;
    }

    public static bool IsTaskLike(ITypeSymbol type, Compilation compilation)
        => IsTaskLike(type, compilation, out _);

    public static bool IsGenericTask(ITypeSymbol type, Compilation compilation, out ITypeSymbol inner)
        => TryGenericTaskLike(type, compilation, "System.Threading.Tasks.Task`1", out inner);

    public static bool IsGenericValueTask(ITypeSymbol type, Compilation compilation, out ITypeSymbol inner)
        => TryGenericTaskLike(type, compilation, "System.Threading.Tasks.ValueTask`1", out inner);

    public static bool IsValueTask(ITypeSymbol type, Compilation compilation)
        => type is INamedTypeSymbol named &&
           (IsDefinition(named, compilation, "System.Threading.Tasks.ValueTask") ||
            IsDefinition(named, compilation, "System.Threading.Tasks.ValueTask`1"));

    private static bool TryGenericTaskLike(
        ITypeSymbol type,
        Compilation compilation,
        string metadataName,
        out ITypeSymbol inner)
    {
        if (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named &&
            IsDefinition(named, compilation, metadataName))
        {
            inner = named.TypeArguments[0];
            return true;
        }

        inner = type;
        return false;
    }

    private static bool IsDefinition(
        INamedTypeSymbol candidate,
        Compilation compilation,
        string metadataName)
        => compilation.GetTypeByMetadataName(metadataName) is { } expected &&
           expected.Locations.Any(static location => location.IsInMetadata) &&
           SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, expected);
}
