using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncArgumentWriterSource
{
    public static string WriteExpression(ITypeSymbol type, string expression)
        => type.SpecialType switch
        {
            SpecialType.System_Boolean => $"global::DotBoxD.Plugins.KernelRpcValue.Bool({expression})",
            SpecialType.System_Int32 => $"global::DotBoxD.Plugins.KernelRpcValue.Int32({expression})",
            SpecialType.System_Int64 => $"global::DotBoxD.Plugins.KernelRpcValue.Int64({expression})",
            SpecialType.System_Double => $"global::DotBoxD.Plugins.KernelRpcValue.Double({expression})",
            SpecialType.System_Single => $"global::DotBoxD.Plugins.KernelRpcValue.Double({expression})",
            SpecialType.System_String => $"global::DotBoxD.Plugins.KernelRpcValue.String({expression})",
            _ => WriteComplexExpression(type, expression)
        };

    private static string WriteComplexExpression(ITypeSymbol type, string expression)
    {
        if (DotBoxDRpcTypeMapper.IsGuid(type))
        {
            return $"global::DotBoxD.Plugins.KernelRpcValue.Guid({expression})";
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            return DotBoxDRpcTypeMapper.EnumUsesI64(enumType)
                ? $"global::DotBoxD.Plugins.KernelRpcValue.Int64(unchecked((long){expression}))"
                : $"global::DotBoxD.Plugins.KernelRpcValue.Int32(unchecked((int){expression}))";
        }

        if (DotBoxDRpcTypeMapper.ListElementType(type) is { } elementType)
        {
            return WriteListExpression(elementType, expression);
        }

        if (DotBoxDRpcTypeMapper.MapTypes(type) is { } map)
        {
            return WriteMapExpression(map.Key, map.Value, expression);
        }

        if (type is INamedTypeSymbol named && DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            return WriteRecordExpression(named, expression);
        }

        throw new NotSupportedException(
            $"InvokeAsync capture type '{type.ToDisplayString()}' is not supported.");
    }

    private static string WriteRecordExpression(INamedTypeSymbol type, string expression)
    {
        var fields = DotBoxDRpcTypeMapper.RecordFields(type);
        var values = new string[fields.Count];
        for (var i = 0; i < fields.Count; i++)
        {
            values[i] = WriteExpression(fields[i].Type, expression + "." + fields[i].Name);
        }

        return "global::DotBoxD.Plugins.KernelRpcValue.Record(new global::DotBoxD.Plugins.KernelRpcValue[] { " +
               string.Join(", ", values) +
               " })";
    }

    private static string WriteListExpression(ITypeSymbol elementType, string expression)
        => "global::DotBoxD.Plugins.KernelRpcValue.List(" +
           "global::System.Linq.Enumerable.ToArray(" +
           "global::System.Linq.Enumerable.Select(" + expression + ", static __item => " +
           WriteExpression(elementType, "__item") + ")))";

    private static string WriteMapExpression(ITypeSymbol keyType, ITypeSymbol valueType, string expression)
        => "global::DotBoxD.Plugins.KernelRpcValue.Map(" +
           "global::System.Linq.Enumerable.ToArray(" +
           "global::System.Linq.Enumerable.SelectMany(" + expression + ", static __entry => " +
           "new global::DotBoxD.Plugins.KernelRpcValue[] { " +
           WriteExpression(keyType, "__entry.Key") + ", " +
           WriteExpression(valueType, "__entry.Value") + " })))";
}
