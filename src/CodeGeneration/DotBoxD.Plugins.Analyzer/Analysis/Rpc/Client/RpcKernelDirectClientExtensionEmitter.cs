namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Text;
using Microsoft.CodeAnalysis;

internal static class RpcKernelDirectClientExtensionEmitter
{
    public static string Emit(
        INamedTypeSymbol kernelType,
        RpcServerExtensionGraft graft,
        IMethodSymbol kernelMethod,
        RpcKernelClientMethodExtension methodExtension,
        Compilation compilation)
        => new Writer(kernelType, graft, kernelMethod, methodExtension, compilation).Emit();

    private sealed class Writer(
        INamedTypeSymbol kernelType,
        RpcServerExtensionGraft graft,
        IMethodSymbol kernelMethod,
        RpcKernelClientMethodExtension methodExtension,
        Compilation compilation)
    {
        private readonly RpcKernelValueConversionEmitter _conv = new(compilation);

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
            var cancellationToken = CancellationTokenParameterName(kernelMethod, receiver);
            var locals = new RpcGeneratedLocalNames(kernelMethod);
            locals.Reserve(receiver);
            locals.Reserve(cancellationToken);
            var accessor = locals.Next("__accessor");
            var arguments = locals.Next("__arguments");
            var request = locals.Next("__request");
            var response = locals.Next("__response");
            var result = locals.Next("__result");
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

            builder.Append(", global::System.Threading.CancellationToken ")
                .Append(cancellationToken).Append(" = default");
            builder.AppendLine(")");
            builder.AppendLine("    {");
            builder.Append("        if (").Append(receiver).Append(" is not global::DotBoxD.Abstractions.IServerExtensionClientAccessor ")
                .Append(accessor).AppendLine(")");
            builder.AppendLine("        {");
            builder.AppendLine("            throw new global::System.InvalidOperationException(\"Server extension calls require a generated plugin facade receiver.\");");
            builder.AppendLine("        }");

            builder.Append("        var ").Append(arguments).Append(" = new global::DotBoxD.Plugins.KernelRpcValue[")
                .Append(userParameterCount + argumentOffset).AppendLine("];");
            if (hasReceiverId)
            {
                builder.Append("        ").Append(arguments).Append("[0] = global::DotBoxD.Plugins.KernelRpcValue.String(")
                    .Append(receiver).AppendLine(".Id);");
            }

            for (var i = 0; i < userParameterCount; i++)
            {
                var parameter = kernelMethod.Parameters[i];
                builder.Append("        ").Append(arguments).Append('[').Append(i + argumentOffset).Append("] = ")
                    .Append(_conv.WriteExpression(parameter.Type, RpcKernelClientParameterSource.Identifier(parameter.Name)))
                    .AppendLine(";");
            }

            builder.Append("        var ").Append(request)
                .Append(" = global::DotBoxD.Plugins.KernelRpcBinaryCodec.EncodeArguments(")
                .Append(arguments).AppendLine(");");
            if (payloadReturnType is null)
            {
                AppendInvoke(builder, isAsyncReturn, accessor, request, response, cancellationToken, assignResponse: true);
                builder.Append("        global::DotBoxD.Plugins.KernelRpcBinaryCodec.DecodeValue(").Append(response)
                    .AppendLine(").RequireKind(global::DotBoxD.Plugins.KernelRpcValueKind.Unit);");
                builder.AppendLine("        return;");
            }
            else
            {
                AppendInvoke(builder, isAsyncReturn, accessor, request, response, cancellationToken, assignResponse: true);
                builder.Append("        var ").Append(result)
                    .Append(" = global::DotBoxD.Plugins.KernelRpcBinaryCodec.DecodeValue(")
                    .Append(response).AppendLine(");");
                builder.Append("        return ").Append(_conv.ReadExpression(payloadReturnType, result)).AppendLine(";");
            }

            builder.AppendLine("    }");
            builder.AppendLine();
        }

        private void AppendInvoke(
            StringBuilder builder,
            bool isAsyncReturn,
            string accessor,
            string request,
            string response,
            string cancellationToken,
            bool assignResponse)
        {
            builder.Append("        ");
            if (assignResponse)
            {
                builder.Append("var ").Append(response).Append(" = ");
            }

            if (isAsyncReturn)
            {
                builder.Append("await ").Append(accessor).Append(".ServerExtensions.InvokeServerExtensionAsync(");
                builder.Append(accessor).Append(".ServerExtensions.PluginId<").Append(TypeName(kernelType))
                    .Append(">(), ").Append(request).Append(", ").Append(cancellationToken)
                    .AppendLine(").ConfigureAwait(false);");
                return;
            }

            builder.Append(accessor).Append(".ServerExtensions.InvokeServerExtensionAsync(");
            builder.Append(accessor).Append(".ServerExtensions.PluginId<").Append(TypeName(kernelType))
                .Append(">(), ").Append(request).Append(", ").Append(cancellationToken)
                .AppendLine(").AsTask().GetAwaiter().GetResult();");
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

        private static string CancellationTokenParameterName(IMethodSymbol method, string receiverName)
        {
            const string seed = "cancellationToken";
            if (!HasParameter(method, seed) && !string.Equals(receiverName, seed, StringComparison.Ordinal))
            {
                return seed;
            }

            for (var suffix = 0; ; suffix++)
            {
                var candidate = seed + "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!HasParameter(method, candidate) &&
                    !string.Equals(receiverName, candidate, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
        }

        private static bool HasParameter(IMethodSymbol method, string name)
            => method.Parameters.Any(parameter => string.Equals(parameter.Name, name, StringComparison.Ordinal));
    }
}
