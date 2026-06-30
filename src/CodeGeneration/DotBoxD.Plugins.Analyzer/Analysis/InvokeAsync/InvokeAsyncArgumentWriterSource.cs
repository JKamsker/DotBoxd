using System.Globalization;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncArgumentWriterSource
{
    public static string WriteExpression(ITypeSymbol type, string expression)
        => WriteExpression(type, expression, depth: 0);

    private static string WriteExpression(ITypeSymbol type, string expression, int depth)
        => type.SpecialType switch
        {
            SpecialType.System_Boolean => $"global::DotBoxD.Plugins.KernelRpcValue.Bool({expression})",
            SpecialType.System_Int32 => $"global::DotBoxD.Plugins.KernelRpcValue.Int32({expression})",
            SpecialType.System_Int64 => $"global::DotBoxD.Plugins.KernelRpcValue.Int64({expression})",
            SpecialType.System_Double => $"global::DotBoxD.Plugins.KernelRpcValue.Double({expression})",
            SpecialType.System_Single => $"global::DotBoxD.Plugins.KernelRpcValue.Double({expression})",
            SpecialType.System_String => $"global::DotBoxD.Plugins.KernelRpcValue.String({expression})",
            _ => WriteComplexExpression(type, expression, depth)
        };

    private static string WriteComplexExpression(ITypeSymbol type, string expression, int depth)
    {
        if (DotBoxDRpcTypeMapper.IsGuid(type))
        {
            return $"global::DotBoxD.Plugins.KernelRpcValue.Guid({expression})";
        }

        if (DotBoxDRpcTypeMapper.IsDateTimeWireType(type))
        {
            return IsDateTimeOffset(type)
                ? WriteDateTimeOffsetExpression(expression, depth)
                : WriteDateTimeExpression(expression, depth);
        }

        if (DotBoxDRpcTypeMapper.IsTimeSpanWireType(type))
        {
            return $"global::DotBoxD.Plugins.KernelRpcValue.Int64({expression}.Ticks)";
        }

        if (DotBoxDRpcTypeMapper.IsDateOnlyWireType(type))
        {
            return $"global::DotBoxD.Plugins.KernelRpcValue.Int32({expression}.DayNumber)";
        }

        if (DotBoxDRpcTypeMapper.IsTimeOnlyWireType(type))
        {
            return $"global::DotBoxD.Plugins.KernelRpcValue.Int64({expression}.Ticks)";
        }

        if (DotBoxDRpcTypeMapper.IsIndexWireType(type))
        {
            return WriteIndexExpression(expression);
        }

        if (DotBoxDRpcTypeMapper.IsRangeWireType(type))
        {
            return WriteRangeExpression(expression);
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            return DotBoxDRpcTypeMapper.EnumUsesI64(enumType)
                ? $"global::DotBoxD.Plugins.KernelRpcValue.Int64(unchecked((long){expression}))"
                : $"global::DotBoxD.Plugins.KernelRpcValue.Int32(unchecked((int){expression}))";
        }

        if (DotBoxDRpcTypeMapper.ListElementType(type) is { } elementType)
        {
            return WriteListExpression(type, elementType, expression, depth);
        }

        if (DotBoxDRpcTypeMapper.MapTypes(type) is { } map)
        {
            return WriteMapExpression(type, map.Key, map.Value, expression, depth);
        }

        if (type is INamedTypeSymbol named && DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            return WriteRecordExpression(named, expression, depth);
        }

        throw new NotSupportedException(
            $"InvokeAsync capture type '{type.ToDisplayString()}' is not supported.");
    }

    private static string WriteRecordExpression(INamedTypeSymbol type, string expression, int depth)
    {
        var fields = DotBoxDRpcTypeMapper.RecordFields(type);
        var values = new string[fields.Count];
        for (var i = 0; i < fields.Count; i++)
        {
            values[i] = WriteExpression(
                fields[i].Type,
                expression + "." + InvokeAsyncSourceIdentifier.Escape(fields[i].Name),
                depth + 1);
        }

        return "global::DotBoxD.Plugins.KernelRpcValue.Record(new global::DotBoxD.Plugins.KernelRpcValue[] { " +
               string.Join(", ", values) +
               " })";
    }

    private static string WriteListExpression(ITypeSymbol listType, ITypeSymbol elementType, string expression, int depth)
    {
        var value = Local("value", depth);
        var item = Local("item", depth);
        var itemExpression = WriteExpression(elementType, item, depth + 1);
        var body = DotBoxDRpcTypeMapper.SupportsIndexedListWrite(listType)
            ? IndexedListBody(listType, value, item, itemExpression, depth)
            : EnumerableListBody(value, item, itemExpression, depth);

        return "((global::System.Func<" + TypeName(listType) + ", global::DotBoxD.Plugins.KernelRpcValue>)(static " +
               value + " => { global::System.ArgumentNullException.ThrowIfNull(" + value + "); " +
               body + " }))(" + expression + ")";
    }

    private static string IndexedListBody(
        ITypeSymbol listType,
        string value,
        string item,
        string itemExpression,
        int depth)
    {
        var count = Local("count", depth);
        var items = Local("items", depth);
        var index = Local("index", depth);
        var countExpression = listType is IArrayTypeSymbol ? value + ".Length" : value + ".Count";
        return "var " + count + " = " + countExpression + "; " +
               "var " + items + " = " + count + " == 0 ? " +
               "global::System.Array.Empty<global::DotBoxD.Plugins.KernelRpcValue>() : " +
               "new global::DotBoxD.Plugins.KernelRpcValue[" + count + "]; " +
               "for (var " + index + " = 0; " + index + " < " + count + "; " + index + "++) { var " +
               item + " = " + value + "[" + index + "]; " +
               items + "[" + index + "] = " + itemExpression + "; } " +
               "return global::DotBoxD.Plugins.KernelRpcValue.List(" + items + ");";
    }

    private static string EnumerableListBody(string value, string item, string itemExpression, int depth)
    {
        var count = Local("count", depth);
        var items = Local("items", depth);
        var index = Local("index", depth);
        var fallbackItems = Local("fallbackItems", depth);
        return "if (global::System.Linq.Enumerable.TryGetNonEnumeratedCount(" + value + ", out var " + count + ")) { " +
               "var " + items + " = " + count + " == 0 ? " +
               "global::System.Array.Empty<global::DotBoxD.Plugins.KernelRpcValue>() : " +
               "new global::DotBoxD.Plugins.KernelRpcValue[" + count + "]; " +
               "var " + index + " = 0; foreach (var " + item + " in " + value + ") { " +
               "if (" + index + " >= " + items + ".Length) { global::System.Array.Resize(ref " + items +
               ", checked(" + index + " + 1)); } " +
               items + "[" + index + "++] = " + itemExpression + "; } " +
               "if (" + index + " != " + items + ".Length) { global::System.Array.Resize(ref " + items + ", " +
               index + "); } return global::DotBoxD.Plugins.KernelRpcValue.List(" + items + "); } " +
               "var " + fallbackItems + " = new global::System.Collections.Generic.List<global::DotBoxD.Plugins.KernelRpcValue>(); " +
               "foreach (var " + item + " in " + value + ") { " + fallbackItems + ".Add(" + itemExpression + "); } " +
               "return global::DotBoxD.Plugins.KernelRpcValue.List(" + fallbackItems + ".ToArray());";
    }

    private static string WriteMapExpression(
        ITypeSymbol mapType,
        ITypeSymbol keyType,
        ITypeSymbol valueType,
        string expression,
        int depth)
    {
        var value = Local("value", depth);
        var pair = Local("pair", depth);
        var entryCount = Local("entryCount", depth);
        var entries = Local("entries", depth);
        var index = Local("index", depth);
        var keyExpression = WriteExpression(keyType, pair + ".Key", depth + 1);
        var valueExpression = WriteExpression(valueType, pair + ".Value", depth + 1);

        return "((global::System.Func<" + TypeName(mapType) + ", global::DotBoxD.Plugins.KernelRpcValue>)(static " +
               value + " => { global::System.ArgumentNullException.ThrowIfNull(" + value + "); " +
               "var " + entryCount + " = " + value + ".Count * 2; " +
               "var " + entries + " = " + entryCount + " == 0 ? " +
               "global::System.Array.Empty<global::DotBoxD.Plugins.KernelRpcValue>() : " +
               "new global::DotBoxD.Plugins.KernelRpcValue[" + entryCount + "]; " +
               "var " + index + " = 0; foreach (var " + pair + " in " + value + ") { " +
               entries + "[" + index + "++] = " + keyExpression + "; " +
               entries + "[" + index + "++] = " + valueExpression + "; } " +
               "return global::DotBoxD.Plugins.KernelRpcValue.Map(" + entries + "); }))(" + expression + ")";
    }

    private static string WriteDateTimeOffsetExpression(string expression, int depth)
    {
        var value = Local("dateTimeOffset", depth);
        return "((global::System.Func<global::System.DateTimeOffset, global::DotBoxD.Plugins.KernelRpcValue>)(static " +
               value + " => " + DateTimeOffsetRecordExpression(value) + "))(" + expression + ")";
    }

    private static string WriteDateTimeExpression(string expression, int depth)
    {
        var value = Local("dateTime", depth);
        var offset = Local("offset", depth);
        return "((global::System.Func<global::System.DateTime, global::DotBoxD.Plugins.KernelRpcValue>)(static " + value + " => " +
           "{ if (" + value + ".Kind != global::System.DateTimeKind.Unspecified) { " +
           "throw new global::System.NotSupportedException(\"InvokeAsync DateTime values must use DateTimeKind.Unspecified; use System.DateTimeOffset to preserve offset or UTC/local semantics.\"); } " +
           "var " + offset + " = new global::System.DateTimeOffset(" + value + ", global::System.TimeSpan.Zero); " +
           "return " + DateTimeOffsetRecordExpression(offset) + "; }))(" +
           expression + ")";
    }

    private static string DateTimeOffsetRecordExpression(string value)
        => "global::DotBoxD.Plugins.KernelRpcValue.Record(new global::DotBoxD.Plugins.KernelRpcValue[] { " +
           "global::DotBoxD.Plugins.KernelRpcValue.Int64(" + value + ".UtcTicks), " +
           "global::DotBoxD.Plugins.KernelRpcValue.Int64(" + value + ".Offset.Ticks) })";

    private static string WriteIndexExpression(string expression)
        => "global::DotBoxD.Plugins.KernelRpcValue.Record(new global::DotBoxD.Plugins.KernelRpcValue[] { " +
           "global::DotBoxD.Plugins.KernelRpcValue.Int32(" + expression + ".Value), " +
           "global::DotBoxD.Plugins.KernelRpcValue.Bool(" + expression + ".IsFromEnd) })";

    private static string WriteRangeExpression(string expression)
        => "global::DotBoxD.Plugins.KernelRpcValue.Record(new global::DotBoxD.Plugins.KernelRpcValue[] { " +
           WriteIndexExpression(expression + ".Start") + ", " +
           WriteIndexExpression(expression + ".End") + " })";

    private static bool IsDateTimeOffset(ITypeSymbol type)
        => type is INamedTypeSymbol { Name: "DateTimeOffset" };

    private static string Local(string stem, int depth)
        => "__dotboxd_" + stem + depth.ToString(CultureInfo.InvariantCulture);

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
