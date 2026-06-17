namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Collections.Generic;
using System.Text;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

internal static class RpcKernelDirectClientExtensionEmitter
{
    public static string Emit(INamedTypeSymbol kernelType, INamedTypeSymbol receiverType, IMethodSymbol kernelMethod)
        => new Writer(kernelType, receiverType, kernelMethod).Emit();

    private sealed class Writer(
        INamedTypeSymbol kernelType,
        INamedTypeSymbol receiverType,
        IMethodSymbol kernelMethod)
    {
        private readonly StringBuilder _helpers = new();
        private readonly Dictionary<string, string> _readers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _writers = new(StringComparer.Ordinal);
        private int _nextHelper;

        public string Emit()
        {
            var builder = new StringBuilder();
            builder.Append("internal static class ").Append(kernelType.Name).AppendLine("DirectServerExtensionClientExtensions");
            builder.AppendLine("{");
            AppendMethod(builder);
            builder.Append(_helpers);
            builder.AppendLine("}");
            return builder.ToString();
        }

        private void AppendMethod(StringBuilder builder)
        {
            var returnType = TypeName(kernelMethod.ReturnType);
            var isAsyncReturn = TryGetPayloadReturnType(kernelMethod.ReturnType, out var payloadReturnType);
            var hasReceiverId = HasReceiverId(receiverType);
            var userParameterCount = kernelMethod.Parameters.Length - 1;
            var argumentOffset = hasReceiverId ? 1 : 0;
            builder.Append("    public static ");
            if (isAsyncReturn)
            {
                builder.Append("async ");
            }

            builder.Append(returnType).Append(' ')
                .Append(Identifier(kernelMethod.Name)).Append("(this ").Append(TypeName(receiverType)).Append(" value");
            for (var i = 0; i < userParameterCount; i++)
            {
                var parameter = kernelMethod.Parameters[i];
                builder.Append(", ").Append(TypeName(parameter.Type)).Append(' ').Append(Identifier(parameter.Name));
            }

            builder.AppendLine(")");
            builder.AppendLine("    {");
            builder.AppendLine("        if (value is not global::DotBoxD.Abstractions.IServerExtensionClientAccessor __accessor)");
            builder.AppendLine("        {");
            builder.AppendLine("            throw new global::System.InvalidOperationException(\"Server extension calls require a generated plugin facade receiver.\");");
            builder.AppendLine("        }");

            builder.Append("        var __arguments = new global::DotBoxD.Plugins.KernelRpcValue[")
                .Append(userParameterCount + argumentOffset).AppendLine("];");
            if (hasReceiverId)
            {
                builder.AppendLine("        __arguments[0] = global::DotBoxD.Plugins.KernelRpcValue.String(value.Id);");
            }

            for (var i = 0; i < userParameterCount; i++)
            {
                var parameter = kernelMethod.Parameters[i];
                builder.Append("        __arguments[").Append(i + argumentOffset).Append("] = ")
                    .Append(WriteExpression(parameter.Type, Identifier(parameter.Name))).AppendLine(";");
            }

            builder.AppendLine("        var __request = global::DotBoxD.Plugins.KernelRpcBinaryCodec.EncodeArguments(__arguments);");
            if (payloadReturnType is null)
            {
                AppendInvoke(builder, isAsyncReturn, assignResponse: false);
                builder.AppendLine("        return;");
            }
            else
            {
                AppendInvoke(builder, isAsyncReturn, assignResponse: true);
                builder.AppendLine("        var __result = global::DotBoxD.Plugins.KernelRpcBinaryCodec.DecodeValue(__response);");
                builder.Append("        return ").Append(ReadExpression(payloadReturnType, "__result")).AppendLine(";");
            }

            builder.AppendLine("    }");
            builder.AppendLine();
        }

        private void AppendInvoke(StringBuilder builder, bool isAsyncReturn, bool assignResponse)
        {
            builder.Append("        ");
            if (assignResponse)
            {
                builder.Append("var __response = ");
            }

            if (isAsyncReturn)
            {
                builder.Append("await __accessor.ServerExtensions.InvokeServerExtensionAsync(");
                builder.Append("__accessor.ServerExtensions.PluginId<").Append(TypeName(kernelType)).AppendLine(">(), __request).ConfigureAwait(false);");
                return;
            }

            builder.Append("__accessor.ServerExtensions.InvokeServerExtensionAsync(");
            builder.Append("__accessor.ServerExtensions.PluginId<").Append(TypeName(kernelType)).AppendLine(">(), __request).AsTask().GetAwaiter().GetResult();");
        }

        private string WriteExpression(ITypeSymbol type, string expression)
            => type.SpecialType switch
            {
                SpecialType.System_Boolean => $"global::DotBoxD.Plugins.KernelRpcValue.Bool({expression})",
                SpecialType.System_Int32 => $"global::DotBoxD.Plugins.KernelRpcValue.Int32({expression})",
                SpecialType.System_Int64 => $"global::DotBoxD.Plugins.KernelRpcValue.Int64({expression})",
                SpecialType.System_Double => $"global::DotBoxD.Plugins.KernelRpcValue.Double({expression})",
                SpecialType.System_String => $"global::DotBoxD.Plugins.KernelRpcValue.String({expression})",
                _ => WriteComplexExpression(type, expression)
            };

        private string ReadExpression(ITypeSymbol type, string expression)
            => type.SpecialType switch
            {
                SpecialType.System_Boolean => $"{expression}.BoolValue",
                SpecialType.System_Int32 => $"{expression}.Int32Value",
                SpecialType.System_Int64 => $"{expression}.Int64Value",
                SpecialType.System_Double => $"{expression}.DoubleValue",
                SpecialType.System_String => $"{expression}.TextValue",
                _ => ReadComplexExpression(type, expression)
            };

        private string WriteComplexExpression(ITypeSymbol type, string expression)
        {
            if (DotBoxDRpcTypeMapper.ListElementType(type) is not null)
            {
                return $"{EnsureListWriter(type)}({expression})";
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
            _helpers.AppendLine("        var __items = new global::System.Collections.Generic.List<global::DotBoxD.Plugins.KernelRpcValue>();");
            _helpers.AppendLine("        foreach (var __item in value)");
            _helpers.AppendLine("        {");
            _helpers.Append("            __items.Add(").Append(itemExpression).AppendLine(");");
            _helpers.AppendLine("        }");
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
            var itemExpression = ReadExpression(elementType, "__source[i]");
            _helpers.Append("    private static global::System.Collections.Generic.List<").Append(TypeName(elementType)).Append("> ")
                .Append(method).AppendLine("(global::DotBoxD.Plugins.KernelRpcValue value)");
            _helpers.AppendLine("    {");
            _helpers.AppendLine("        value.RequireKind(global::DotBoxD.Plugins.KernelRpcValueKind.List);");
            _helpers.AppendLine("        var __source = value.Items;");
            _helpers.Append("        var __result = new global::System.Collections.Generic.List<").Append(TypeName(elementType)).AppendLine(">(__source.Length);");
            _helpers.AppendLine("        for (var i = 0; i < __source.Length; i++)");
            _helpers.AppendLine("        {");
            _helpers.Append("            __result.Add(").Append(itemExpression).AppendLine(");");
            _helpers.AppendLine("        }");
            _helpers.AppendLine("        return __result;");
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
            _helpers.Append("    private static ").Append(TypeName(type)).Append(' ').Append(method)
                .AppendLine("(global::DotBoxD.Plugins.KernelRpcValue value)");
            _helpers.AppendLine("    {");
            _helpers.AppendLine("        value.RequireKind(global::DotBoxD.Plugins.KernelRpcValueKind.Record);");
            _helpers.AppendLine("        var __fields = value.Items;");
            _helpers.Append("        return new ").Append(TypeName(type)).Append('(');
            for (var i = 0; i < fields.Count; i++)
            {
                if (i > 0)
                {
                    _helpers.Append(", ");
                }

                _helpers.Append(ReadExpression(fields[i].Type, "__fields[" + i + "]"));
            }
            _helpers.AppendLine(");");
            _helpers.AppendLine("    }");
            _helpers.AppendLine();
            return method;
        }

        private string NextHelperName(string prefix) => prefix + "KernelRpcValue" + _nextHelper++;

        private static string TypeName(ITypeSymbol type)
            => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        private static string TypeKey(ITypeSymbol type)
            => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        private static bool HasReceiverId(INamedTypeSymbol type)
        {
            if (HasStringIdProperty(type))
            {
                return true;
            }

            foreach (var inherited in type.AllInterfaces)
            {
                if (HasStringIdProperty(inherited))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasStringIdProperty(INamedTypeSymbol type)
        {
            foreach (var member in type.GetMembers("Id"))
            {
                if (member is IPropertySymbol { Parameters.Length: 0, Type.SpecialType: SpecialType.System_String })
                {
                    return true;
                }
            }

            return false;
        }

        private static string Identifier(string name) => "@" + name;

        private static bool TryGetPayloadReturnType(ITypeSymbol returnType, out ITypeSymbol? payloadReturnType)
        {
            if (returnType.SpecialType == SpecialType.System_Void)
            {
                payloadReturnType = null;
                return false;
            }

            if (returnType is INamedTypeSymbol
                {
                    Name: "Task" or "ValueTask",
                    ContainingNamespace: { } ns
                } taskLike &&
                string.Equals(ns.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal))
            {
                payloadReturnType = taskLike is { IsGenericType: true, TypeArguments.Length: 1 }
                    ? taskLike.TypeArguments[0]
                    : null;
                return true;
            }

            payloadReturnType = returnType;
            return false;
        }
    }
}
