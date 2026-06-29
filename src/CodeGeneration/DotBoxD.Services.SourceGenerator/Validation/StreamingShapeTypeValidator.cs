using System;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static class StreamingShapeTypeValidator
{
    public static bool ContainsConcreteStreamingShape(
        ITypeSymbol type,
        CancellationToken ct,
        out string replacement)
    {
        ct.ThrowIfCancellationRequested();

        if (type is IArrayTypeSymbol array)
        {
            return ContainsConcreteStreamingShape(array.ElementType, ct, out replacement);
        }

        if (type is not INamedTypeSymbol named)
        {
            replacement = string.Empty;
            return false;
        }

        if (InheritsFrom(named, IsStream))
        {
            replacement = "System.IO.Stream";
            return true;
        }

        if (InheritsFrom(named, IsPipe))
        {
            replacement = "System.IO.Pipelines.Pipe";
            return true;
        }

        if (!IsAsyncEnumerable(named) && ImplementsAsyncEnumerable(named, ct))
        {
            replacement = "IAsyncEnumerable<T>";
            return true;
        }

        foreach (var arg in named.TypeArguments)
        {
            ct.ThrowIfCancellationRequested();

            if (ContainsConcreteStreamingShape(arg, ct, out replacement))
            {
                return true;
            }
        }

        replacement = string.Empty;
        return false;
    }

    private static bool InheritsFrom(INamedTypeSymbol type, Func<INamedTypeSymbol, bool> isTarget)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (isTarget(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ImplementsAsyncEnumerable(INamedTypeSymbol type, CancellationToken ct)
    {
        foreach (var candidate in type.AllInterfaces)
        {
            ct.ThrowIfCancellationRequested();

            if (IsAsyncEnumerable(candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsStream(INamedTypeSymbol type) =>
        type.Name == "Stream" &&
        type.ContainingNamespace?.ToDisplayString() == ServicesGeneratorTypeNames.SystemIoNamespace;

    private static bool IsPipe(INamedTypeSymbol type) =>
        type.Name == "Pipe" &&
        type.ContainingNamespace?.ToDisplayString() == ServicesGeneratorTypeNames.SystemIoPipelinesNamespace;

    private static bool IsAsyncEnumerable(INamedTypeSymbol type) =>
        type.Name == "IAsyncEnumerable" &&
        type.ContainingNamespace?.ToDisplayString() == ServicesGeneratorTypeNames.SystemCollectionsGenericNamespace;
}
