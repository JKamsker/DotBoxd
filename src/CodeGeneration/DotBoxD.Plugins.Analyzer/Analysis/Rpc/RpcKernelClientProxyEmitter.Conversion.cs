namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Collections.Generic;
using Microsoft.CodeAnalysis;

internal static partial class RpcKernelClientProxyEmitter
{
    private sealed partial class ProxySourceWriter
    {
        private string WriteExpression(ITypeSymbol type, string expression)
        {
            return type.SpecialType switch
            {
                SpecialType.System_Boolean => $"global::DotBoxD.Plugins.KernelRpcValue.Bool({expression})",
                SpecialType.System_Int32 => $"global::DotBoxD.Plugins.KernelRpcValue.Int32({expression})",
                SpecialType.System_Int64 => $"global::DotBoxD.Plugins.KernelRpcValue.Int64({expression})",
                SpecialType.System_Double => $"global::DotBoxD.Plugins.KernelRpcValue.Double({expression})",
                SpecialType.System_String => $"global::DotBoxD.Plugins.KernelRpcValue.String({expression})",
                _ => WriteComplexExpression(type, expression)
            };
        }

        private string ReadExpression(ITypeSymbol type, string expression)
        {
            return type.SpecialType switch
            {
                SpecialType.System_Boolean => $"{expression}.BoolValue",
                SpecialType.System_Int32 => $"{expression}.Int32Value",
                SpecialType.System_Int64 => $"{expression}.Int64Value",
                SpecialType.System_Double => $"{expression}.DoubleValue",
                SpecialType.System_String => $"{expression}.TextValue",
                _ => ReadComplexExpression(type, expression)
            };
        }

        private string WriteComplexExpression(ITypeSymbol type, string expression)
        {
            if (DotBoxDRpcTypeMapper.ListElementType(type) is not null)
            {
                return $"{EnsureListWriter(type)}({expression})";
            }

            if (type is INamedTypeSymbol named && DotBoxDRpcTypeMapper.IsRecordDto(named))
            {
                return $"{EnsureDtoWriter(named)}({expression})";
            }

            throw new NotSupportedException($"Server extension type '{type.ToDisplayString()}' is not supported.");
        }

        private string ReadComplexExpression(ITypeSymbol type, string expression)
        {
            if (DotBoxDRpcTypeMapper.ListElementType(type) is not null)
            {
                return $"{EnsureListReader(type)}({expression})";
            }

            if (type is INamedTypeSymbol named && DotBoxDRpcTypeMapper.IsRecordDto(named))
            {
                return $"{EnsureDtoReader(named)}({expression})";
            }

            throw new NotSupportedException($"Server extension type '{type.ToDisplayString()}' is not supported.");
        }

        private string EnsureListWriter(ITypeSymbol type)
        {
            var key = TypeKey(type);
            if (_writers.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var method = NextHelperName("Write");
            _writers[key] = method;
            var elementType = DotBoxDRpcTypeMapper.ListElementType(type)!;
            var itemExpression = WriteExpression(elementType, "__item");
            _helpers.Append("    private static global::DotBoxD.Plugins.KernelRpcValue ").Append(method)
                .Append('(').Append(TypeName(type)).AppendLine(" value)");
            _helpers.AppendLine("    {");
            _helpers.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(value);");
            _helpers.AppendLine("        var __items = new global::System.Collections.Generic.List<global::DotBoxD.Plugins.KernelRpcValue>();");
            _helpers.AppendLine("        foreach (var __item in value)");
            _helpers.AppendLine("        {");
            _helpers.Append("            __items.Add(").Append(itemExpression).AppendLine(");");
            _helpers.AppendLine("        }");
            _helpers.AppendLine();
            _helpers.AppendLine("        return global::DotBoxD.Plugins.KernelRpcValue.List(__items.ToArray());");
            _helpers.AppendLine("    }");
            _helpers.AppendLine();
            return method;
        }

        private string EnsureListReader(ITypeSymbol type)
        {
            var key = TypeKey(type);
            if (_readers.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var method = NextHelperName("Read");
            _readers[key] = method;
            var elementType = DotBoxDRpcTypeMapper.ListElementType(type)!;
            var elementName = TypeName(elementType);
            var itemExpression = ReadExpression(elementType, "__source[i]");
            var returnsArray = type is IArrayTypeSymbol;
            var returnType = returnsArray ? TypeName(type) : $"global::System.Collections.Generic.List<{elementName}>";
            _helpers.Append("    private static ").Append(returnType).Append(' ').Append(method)
                .AppendLine("(global::DotBoxD.Plugins.KernelRpcValue value)");
            _helpers.AppendLine("    {");
            _helpers.AppendLine("        value.RequireKind(global::DotBoxD.Plugins.KernelRpcValueKind.List);");
            _helpers.AppendLine("        var __source = value.Items;");
            AppendListReaderBody(elementName, itemExpression, returnsArray);
            _helpers.AppendLine();
            _helpers.AppendLine("        return __result;");
            _helpers.AppendLine("    }");
            _helpers.AppendLine();
            return method;
        }

        private void AppendListReaderBody(string elementName, string itemExpression, bool returnsArray)
        {
            if (returnsArray)
            {
                _helpers.Append("        var __result = new ").Append(elementName).AppendLine("[__source.Length];");
                _helpers.AppendLine("        for (var i = 0; i < __source.Length; i++)");
                _helpers.AppendLine("        {");
                _helpers.Append("            __result[i] = ").Append(itemExpression).AppendLine(";");
                _helpers.AppendLine("        }");
                return;
            }

            _helpers.Append("        var __result = new global::System.Collections.Generic.List<")
                .Append(elementName).AppendLine(">(__source.Length);");
            _helpers.AppendLine("        for (var i = 0; i < __source.Length; i++)");
            _helpers.AppendLine("        {");
            _helpers.Append("            __result.Add(").Append(itemExpression).AppendLine(");");
            _helpers.AppendLine("        }");
        }

        private string EnsureDtoWriter(INamedTypeSymbol type)
        {
            var key = TypeKey(type);
            if (_writers.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var method = NextHelperName("Write");
            _writers[key] = method;
            var fieldExpressions = DtoWriteExpressions(type);
            _helpers.Append("    private static global::DotBoxD.Plugins.KernelRpcValue ").Append(method)
                .Append('(').Append(TypeName(type)).AppendLine(" value)");
            _helpers.AppendLine("    {");
            _helpers.AppendLine("        return global::DotBoxD.Plugins.KernelRpcValue.Record(new global::DotBoxD.Plugins.KernelRpcValue[]");
            _helpers.AppendLine("        {");
            foreach (var fieldExpression in fieldExpressions)
            {
                _helpers.Append("            ").Append(fieldExpression).AppendLine(",");
            }

            _helpers.AppendLine("        });");
            _helpers.AppendLine("    }");
            _helpers.AppendLine();
            return method;
        }

        private string EnsureDtoReader(INamedTypeSymbol type)
        {
            var key = TypeKey(type);
            if (_readers.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var method = NextHelperName("Read");
            _readers[key] = method;
            var fields = DotBoxDRpcTypeMapper.RecordFields(type);
            var constructor = ResolveConstructor(type, fields);
            var constructorArguments = DtoConstructorArguments(fields, constructor);
            _helpers.Append("    private static ").Append(TypeName(type)).Append(' ').Append(method)
                .AppendLine("(global::DotBoxD.Plugins.KernelRpcValue value)");
            _helpers.AppendLine("    {");
            _helpers.AppendLine("        value.RequireKind(global::DotBoxD.Plugins.KernelRpcValueKind.Record);");
            _helpers.AppendLine("        var __fields = value.Items;");
            _helpers.Append("        if (__fields.Length != ").Append(fields.Count).AppendLine(")");
            _helpers.AppendLine("        {");
            _helpers.AppendLine("            throw new global::System.NotSupportedException(\"Server extension record field count did not match the generated DTO shape.\");");
            _helpers.AppendLine("        }");
            _helpers.AppendLine();
            _helpers.Append("        return new ").Append(TypeName(type)).Append('(');
            _helpers.Append(string.Join(", ", constructorArguments));
            _helpers.AppendLine(");");
            _helpers.AppendLine("    }");
            _helpers.AppendLine();
            return method;
        }

        private List<string> DtoWriteExpressions(INamedTypeSymbol type)
        {
            var fields = DotBoxDRpcTypeMapper.RecordFields(type);
            var expressions = new List<string>(fields.Count);
            foreach (var field in fields)
            {
                expressions.Add(WriteExpression(field.Type, "value." + Identifier(field.Name)));
            }

            return expressions;
        }

        private List<string> DtoConstructorArguments(
            IReadOnlyList<IPropertySymbol> fields,
            IMethodSymbol constructor)
        {
            var arguments = new List<string>(constructor.Parameters.Length);
            foreach (var parameter in constructor.Parameters)
            {
                var fieldIndex = FieldIndex(fields, parameter.Name);
                arguments.Add(ReadExpression(fields[fieldIndex].Type, "__fields[" + fieldIndex + "]"));
            }

            return arguments;
        }

        private static IMethodSymbol ResolveConstructor(INamedTypeSymbol type, IReadOnlyList<IPropertySymbol> fields)
        {
            foreach (var constructor in type.InstanceConstructors)
            {
                if (constructor.Parameters.Length != fields.Count || constructor.Parameters.Length == 0)
                {
                    continue;
                }

                var matched = true;
                foreach (var parameter in constructor.Parameters)
                {
                    if (FieldIndex(fields, parameter.Name) < 0)
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return constructor;
                }
            }

            throw new NotSupportedException(
                $"Server extension DTO '{type.ToDisplayString()}' must expose a constructor matching its public fields.");
        }

        private static int FieldIndex(IReadOnlyList<IPropertySymbol> fields, string? name)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                if (string.Equals(fields[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
