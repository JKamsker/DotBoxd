using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class TupleElementNameComparer
{
    public static bool HasSameElementNames(
        IMethodSymbol left,
        IMethodSymbol right,
        CancellationToken ct)
    {
        if (!HasSameElementNames(left.ReturnType, right.ReturnType, ct) ||
            left.Parameters.Length != right.Parameters.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Parameters.Length; i++)
        {
            if (!HasSameElementNames(left.Parameters[i].Type, right.Parameters[i].Type, ct))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasSameElementNames(
        ITypeSymbol left,
        ITypeSymbol right,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (left is IArrayTypeSymbol leftArray && right is IArrayTypeSymbol rightArray)
        {
            return leftArray.Rank == rightArray.Rank &&
                HasSameElementNames(leftArray.ElementType, rightArray.ElementType, ct);
        }

        if (left is INamedTypeSymbol leftNamed && right is INamedTypeSymbol rightNamed)
        {
            return HasSameElementNames(leftNamed, rightNamed, ct);
        }

        return true;
    }

    private static bool HasSameElementNames(
        INamedTypeSymbol left,
        INamedTypeSymbol right,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (IsTupleCompatible(left) || IsTupleCompatible(right))
        {
            return HasSameTupleFields(left, right, ct);
        }

        if (left.TypeArguments.Length != right.TypeArguments.Length)
        {
            return false;
        }

        for (var i = 0; i < left.TypeArguments.Length; i++)
        {
            if (!HasSameElementNames(left.TypeArguments[i], right.TypeArguments[i], ct))
            {
                return false;
            }
        }

        if (left.ContainingType is null || right.ContainingType is null)
        {
            return left.ContainingType is null && right.ContainingType is null;
        }

        return HasSameElementNames(left.ContainingType, right.ContainingType, ct);
    }

    private static bool HasSameTupleFields(
        INamedTypeSymbol left,
        INamedTypeSymbol right,
        CancellationToken ct)
    {
        var leftElements = GetTupleElements(left, ct);
        var rightElements = GetTupleElements(right, ct);
        if (leftElements.Count != rightElements.Count)
        {
            return false;
        }

        for (var i = 0; i < leftElements.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (leftElements[i].Name != rightElements[i].Name ||
                !HasSameElementNames(leftElements[i].Type, rightElements[i].Type, ct))
            {
                return false;
            }
        }

        return true;
    }

    private static List<TupleElementInfo> GetTupleElements(
        INamedTypeSymbol type,
        CancellationToken ct)
    {
        var elements = new List<TupleElementInfo>();
        AddTupleElements(type, elements, ct);
        return elements;
    }

    private static void AddTupleElements(
        INamedTypeSymbol type,
        List<TupleElementInfo> elements,
        CancellationToken ct)
    {
        if (type.IsTupleType)
        {
            for (var i = 0; i < type.TupleElements.Length; i++)
            {
                ct.ThrowIfCancellationRequested();

                var element = type.TupleElements[i];
                elements.Add(new TupleElementInfo(GetExplicitTupleElementName(element, i), element.Type));
            }

            return;
        }

        for (var i = 0; i < type.TypeArguments.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            var arg = type.TypeArguments[i];
            if (i == 7 &&
                type.TypeArguments.Length == 8 &&
                arg is INamedTypeSymbol rest &&
                IsValueTuple(rest))
            {
                AddTupleElements(rest, elements, ct);
                continue;
            }

            elements.Add(new TupleElementInfo(string.Empty, arg));
        }
    }

    private static bool IsTupleCompatible(INamedTypeSymbol type) =>
        type.IsTupleType || IsValueTuple(type);

    private static bool IsValueTuple(INamedTypeSymbol type) =>
        type.ContainingNamespace.ToDisplayString() == "System" &&
        type.Name == "ValueTuple" &&
        type.TypeArguments.Length is >= 1 and <= 8;

    private static string GetExplicitTupleElementName(IFieldSymbol element, int index)
    {
        var defaultName = "Item" + (index + 1).ToString(CultureInfo.InvariantCulture);
        return element.Name == defaultName ? string.Empty : element.Name;
    }

    private readonly struct TupleElementInfo
    {
        public TupleElementInfo(string name, ITypeSymbol type)
        {
            Name = name;
            Type = type;
        }

        public string Name { get; }

        public ITypeSymbol Type { get; }
    }
}
