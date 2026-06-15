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
            SpecialType.System_String => $"global::DotBoxD.Plugins.KernelRpcValue.String({expression})",
            _ => WriteComplexExpression(type, expression)
        };

    private static string WriteComplexExpression(ITypeSymbol type, string expression)
    {
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
}
