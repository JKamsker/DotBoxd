namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using Microsoft.CodeAnalysis;

internal sealed partial class RpcKernelPayloadReadEmitter
{
    private string EnsureSingleReader()
    {
        const string key = "scalar:System.Single";
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName();
        _readers[key] = method;
        _helpers.Append("    private static float ").Append(method)
            .AppendLine("(ref global::DotBoxD.Plugins.KernelRpcPayloadReader reader)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        var __value = reader.ReadDouble();");
        _helpers.AppendLine("        var __result = (float)__value;");
        _helpers.AppendLine("        if (global::System.Double.IsFinite(__value) && !global::System.Single.IsFinite(__result))");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            throw new global::System.NotSupportedException(\"Server extension F64 payload cannot be represented as System.Single without overflow.\");");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        _helpers.AppendLine("        return __result;");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }

    private string EnsureEnumReader(INamedTypeSymbol enumType)
    {
        var key = "enum:" + TypeKey(enumType);
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName();
        _readers[key] = method;
        var typeName = TypeName(enumType);
        _helpers.Append("    private static ").Append(typeName).Append(' ').Append(method)
            .AppendLine("(ref global::DotBoxD.Plugins.KernelRpcPayloadReader reader)");
        _helpers.AppendLine("    {");
        if (DotBoxDRpcTypeMapper.EnumUsesI64(enumType))
        {
            _helpers.AppendLine("        var __value = reader.ReadInt64();");
            AppendInt64EnumRangeGuard(enumType);
            AppendInt64EnumReturn(enumType, typeName);
        }
        else
        {
            _helpers.AppendLine("        var __value = reader.ReadInt32();");
            AppendInt32EnumRangeGuard(enumType);
            _helpers.Append("        return unchecked((").Append(typeName).AppendLine(")__value);");
        }

        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }

    private void AppendInt32EnumRangeGuard(INamedTypeSymbol enumType)
    {
        switch (enumType.EnumUnderlyingType?.SpecialType)
        {
            case SpecialType.System_Byte:
                AppendEnumRangeGuard("byte.MinValue", "byte.MaxValue");
                break;
            case SpecialType.System_SByte:
                AppendEnumRangeGuard("sbyte.MinValue", "sbyte.MaxValue");
                break;
            case SpecialType.System_Int16:
                AppendEnumRangeGuard("short.MinValue", "short.MaxValue");
                break;
            case SpecialType.System_UInt16:
                AppendEnumRangeGuard("ushort.MinValue", "ushort.MaxValue");
                break;
        }
    }

    private void AppendInt64EnumRangeGuard(INamedTypeSymbol enumType)
        => RpcEnumRangeGuardSource.AppendInt64EnumRangeGuard(
            _helpers,
            enumType,
            "        ",
            "Server extension enum payload is outside the target enum underlying range.");

    private void AppendEnumRangeGuard(string minimum, string maximum)
    {
        _helpers.Append("        if (__value < ").Append(minimum).Append(" || __value > ").Append(maximum)
            .AppendLine(")");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            throw new global::System.NotSupportedException(\"Server extension enum payload is outside the target enum underlying range.\");");
        _helpers.AppendLine("        }");
    }

    private void AppendInt64EnumReturn(INamedTypeSymbol enumType, string typeName)
    {
        if (enumType.EnumUnderlyingType?.SpecialType == SpecialType.System_UInt32)
        {
            _helpers.Append("        return unchecked((").Append(typeName).AppendLine(")(uint)__value);");
            return;
        }

        if (enumType.EnumUnderlyingType?.SpecialType == SpecialType.System_UInt64)
        {
            _helpers.Append("        return unchecked((").Append(typeName).AppendLine(")(ulong)__value);");
            return;
        }

        _helpers.Append("        return unchecked((").Append(typeName).AppendLine(")__value);");
    }
}
