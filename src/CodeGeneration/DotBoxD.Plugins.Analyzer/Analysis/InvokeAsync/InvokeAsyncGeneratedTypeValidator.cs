using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncGeneratedTypeValidator
{
    private const int MaxTypeDepth = 8;

    public static void Validate(InvokeAsyncCallShape shape, Compilation compilation)
    {
        ValidateType(shape.ReturnType, compilation, "return type", 0, NewVisitingSet());
        foreach (var argumentType in shape.ArgumentTypes)
        {
            ValidateType(argumentType, compilation, "capture type", 0, NewVisitingSet());
        }

        if (shape.CaptureType is { } captureType)
        {
            ValidateType(captureType, compilation, "capture bag type", 0, NewVisitingSet());
        }

        foreach (var syncOut in shape.SyncOuts)
        {
            ValidateType(syncOut.Type, compilation, "capture member '" + syncOut.TargetName + "'", 0, NewVisitingSet());
        }
    }

    private static void ValidateType(
        ITypeSymbol type,
        Compilation compilation,
        string role,
        int depth,
        HashSet<ITypeSymbol> visiting)
    {
        if (DotBoxDNullableScalarType.IsNullableValueType(type) ||
            type.NullableAnnotation == NullableAnnotation.Annotated && type.IsReferenceType)
        {
            throw new NotSupportedException(
                $"InvokeAsync {role} '{type.ToDisplayString()}' cannot be nullable because kernel RPC does not encode null values.");
        }

        if (type is IArrayTypeSymbol array)
        {
            RejectTooDeep(type, role, depth);
            ValidateType(array.ElementType, compilation, role + " element", depth + 1, visiting);
            return;
        }

        if (type.TypeKind == TypeKind.TypeParameter)
        {
            throw new NotSupportedException(
                $"InvokeAsync {role} '{type.ToDisplayString()}' must be a concrete generated-code-accessible type.");
        }

        if (type is not INamedTypeSymbol named)
        {
            return;
        }

        if (named.IsAnonymousType)
        {
            throw new NotSupportedException(
                $"InvokeAsync {role} '{type.ToDisplayString()}' cannot be anonymous because generated interceptors must name the type.");
        }

        if (!compilation.IsSymbolAccessibleWithin(named.OriginalDefinition, compilation.Assembly))
        {
            throw new NotSupportedException(
                $"InvokeAsync {role} '{type.ToDisplayString()}' must be accessible from generated code.");
        }

        foreach (var typeArgument in named.TypeArguments)
        {
            ValidateType(typeArgument, compilation, role + " type argument", depth + 1, visiting);
        }

        if (DotBoxDRpcTypeMapper.IsScalar(type) ||
            DotBoxDRpcTypeMapper.IsGuid(type) ||
            DotBoxDRpcTypeMapper.IsDateTimeWireType(type) ||
            DotBoxDRpcTypeMapper.IsTimeSpanWireType(type) ||
            type.TypeKind == TypeKind.Enum)
        {
            return;
        }

        if (DotBoxDRpcTypeMapper.ListElementType(type) is { } elementType)
        {
            RejectTooDeep(type, role, depth);
            ValidateType(elementType, compilation, role + " element", depth + 1, visiting);
            return;
        }

        if (DotBoxDRpcTypeMapper.MapTypes(type) is { } map)
        {
            RejectTooDeep(type, role, depth);
            ValidateType(map.Key, compilation, role + " key", depth + 1, visiting);
            ValidateType(map.Value, compilation, role + " value", depth + 1, visiting);
            return;
        }

        if (DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            RejectTooDeep(type, role, depth);
            if (!visiting.Add(named))
            {
                throw new NotSupportedException(
                    $"InvokeAsync {role} '{type.ToDisplayString()}' is cyclic; recursive DTO shapes are not supported.");
            }

            try
            {
                foreach (var field in DotBoxDRpcTypeMapper.RecordFields(named))
                {
                    ValidateType(field.Type, compilation, role + " member '" + field.Name + "'", depth + 1, visiting);
                }
            }
            finally
            {
                visiting.Remove(named);
            }
        }
    }

    private static void RejectTooDeep(ITypeSymbol type, string role, int depth)
    {
        if (depth >= MaxTypeDepth)
        {
            throw new NotSupportedException(
                $"InvokeAsync {role} '{type.ToDisplayString()}' exceeds the supported RPC shape depth.");
        }
    }

    private static HashSet<ITypeSymbol> NewVisitingSet()
        => new(SymbolEqualityComparer.Default);
}
