namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Text;
using Microsoft.CodeAnalysis;

internal static class RpcKernelDirectClientExtensionEmitter
{
    public static string Emit(
        INamedTypeSymbol kernelType,
        RpcServerExtensionGraft graft,
        IMethodSymbol kernelMethod,
        RpcKernelClientMethodExtension methodExtension)
        => new Writer(kernelType, graft, kernelMethod, methodExtension).Emit();

    private sealed class Writer(
        INamedTypeSymbol kernelType,
        RpcServerExtensionGraft graft,
        IMethodSymbol kernelMethod,
        RpcKernelClientMethodExtension methodExtension)
    {
        private readonly RpcKernelValueConversionEmitter _conv = new();

        public string Emit()
        {
            var builder = new StringBuilder();
            builder.Append("internal static class ").Append(kernelType.Name).AppendLine("DirectServerExtensionClientExtensions");
            builder.AppendLine("{");
            AppendMethod(builder);
            builder.Append(_conv.Helpers);
            builder.AppendLine("}");
            return builder.ToString();
        }

        private void AppendMethod(StringBuilder builder)
        {
            var returnType = TypeName(kernelMethod.ReturnType);
            var isAsyncReturn = TryGetPayloadReturnType(kernelMethod.ReturnType, out var payloadReturnType);
            var hasReceiverId = graft.InjectsReceiverId;
            var userParameterCount = kernelMethod.Parameters.Length - 1;
            var argumentOffset = hasReceiverId ? 1 : 0;
            var receiver = ReceiverParameterName(kernelMethod);
            builder.Append("    public static ");
            if (isAsyncReturn)
            {
                builder.Append("async ");
            }

            builder.Append(returnType).Append(' ')
                .Append(RpcKernelClientParameterSource.Identifier(methodExtension.Name)).Append("(this ")
                .Append(TypeName(methodExtension.ReceiverType)).Append(' ').Append(receiver);
            for (var i = 0; i < userParameterCount; i++)
            {
                var parameter = kernelMethod.Parameters[i];
                builder.Append(", ").Append(RpcKernelClientParameterSource.Declaration(parameter));
            }

            builder.AppendLine(")");
            builder.AppendLine("    {");
            builder.Append("        if (").Append(receiver).AppendLine(" is not global::DotBoxD.Abstractions.IServerExtensionClientAccessor __accessor)");
            builder.AppendLine("        {");
            builder.AppendLine("            throw new global::System.InvalidOperationException(\"Server extension calls require a generated plugin facade receiver.\");");
            builder.AppendLine("        }");

            builder.Append("        var __arguments = new global::DotBoxD.Plugins.KernelRpcValue[")
                .Append(userParameterCount + argumentOffset).AppendLine("];");
            if (hasReceiverId)
            {
                builder.Append("        __arguments[0] = global::DotBoxD.Plugins.KernelRpcValue.String(")
                    .Append(receiver).AppendLine(".Id);");
            }

            for (var i = 0; i < userParameterCount; i++)
            {
                var parameter = kernelMethod.Parameters[i];
                builder.Append("        __arguments[").Append(i + argumentOffset).Append("] = ")
                    .Append(_conv.WriteExpression(parameter.Type, RpcKernelClientParameterSource.Identifier(parameter.Name)))
                    .AppendLine(";");
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
                builder.Append("        return ").Append(_conv.ReadExpression(payloadReturnType, "__result")).AppendLine(";");
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

        private static string TypeName(ITypeSymbol type)
            => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

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

        private static string ReceiverParameterName(IMethodSymbol method)
        {
            const string seed = "__receiver";
            if (!HasParameter(method, seed))
            {
                return seed;
            }

            for (var suffix = 0; ; suffix++)
            {
                var candidate = seed + "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!HasParameter(method, candidate))
                {
                    return candidate;
                }
            }
        }

        private static bool HasParameter(IMethodSymbol method, string name)
            => method.Parameters.Any(parameter => string.Equals(parameter.Name, name, StringComparison.Ordinal));
    }
}
