namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

internal static partial class RpcKernelClientProxyEmitter
{
    public static string Emit(
        INamedTypeSymbol kernelType,
        IMethodSymbol kernelMethod,
        INamedTypeSymbol serviceType,
        Compilation compilation)
    {
        var serviceMethod = ResolveServiceMethod(serviceType, kernelMethod);
        return Emit(kernelType, serviceType, serviceMethod, compilation);
    }

    public static string Emit(
        INamedTypeSymbol kernelType,
        INamedTypeSymbol serviceType,
        IMethodSymbol serviceMethod,
        Compilation compilation)
    {
        EnsureAccessibleFromGeneratedClient(serviceType);
        if (serviceType.TypeKind != TypeKind.Interface)
        {
            throw new NotSupportedException("Server extension client generation requires an interface contract type.");
        }

        return new ProxySourceWriter(kernelType, serviceType, serviceMethod, compilation).Emit();
    }

    internal static IMethodSymbol ResolveServiceMethod(INamedTypeSymbol serviceType, IMethodSymbol kernelMethod)
    {
        EnsureAccessibleFromGeneratedClient(serviceType);
        return RpcKernelClientServiceMethodResolver.Resolve(serviceType, kernelMethod);
    }

    private static void EnsureAccessibleFromGeneratedClient(INamedTypeSymbol serviceType)
        => RpcGeneratedClientAccessibility.EnsureAccessible(
            serviceType,
            $"Server extension interface '{serviceType.ToDisplayString()}'");

    private static bool IsGenericTask(ITypeSymbol type, out ITypeSymbol inner)
        => TryGenericTaskLike(type, "Task", out inner);

    private static bool IsGenericValueTask(ITypeSymbol type, out ITypeSymbol inner)
        => TryGenericTaskLike(type, "ValueTask", out inner);

    private static bool TryGenericTaskLike(ITypeSymbol type, string name, out ITypeSymbol inner)
    {
        if (type is INamedTypeSymbol
            {
                IsGenericType: true,
                TypeArguments.Length: 1,
                Name: var typeName,
                ContainingNamespace: { } ns
            } named &&
            string.Equals(typeName, name, StringComparison.Ordinal) &&
            string.Equals(ns.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal))
        {
            inner = named.TypeArguments[0];
            return true;
        }

        inner = type;
        return false;
    }

    private enum ReturnShape
    {
        Direct,
        Task,
        ValueTask
    }

    private sealed class ProxySourceWriter
    {
        private readonly INamedTypeSymbol _kernelType;
        private readonly INamedTypeSymbol _serviceType;
        private readonly IMethodSymbol _serviceMethod;
        private readonly ITypeSymbol? _payloadReturnType;
        private readonly ReturnShape _returnShape;
        private readonly RpcKernelValueConversionEmitter _conv;

        public ProxySourceWriter(
            INamedTypeSymbol kernelType,
            INamedTypeSymbol serviceType,
            IMethodSymbol serviceMethod,
            Compilation compilation)
        {
            _kernelType = kernelType;
            _serviceType = serviceType;
            _serviceMethod = serviceMethod;
            _conv = new RpcKernelValueConversionEmitter(compilation);
            _returnShape = Shape(serviceMethod.ReturnType, out _payloadReturnType);
        }

        public string Emit()
        {
            var builder = new StringBuilder();
            var clientName = _kernelType.Name + "ServerExtensionClient";
            builder.Append(AccessibilityKeyword(_serviceType)).Append(" sealed class ")
                .Append(clientName).Append(" : ").AppendLine(TypeName(_serviceType));
            builder.AppendLine("{");
            builder.AppendLine("    private readonly global::DotBoxD.Abstractions.IServerExtensionWireClient _client;");
            builder.AppendLine("    private readonly string _pluginId;");
            builder.AppendLine();
            builder.Append("    public ").Append(clientName)
                .AppendLine("(global::DotBoxD.Abstractions.IServerExtensionWireClient client, string pluginId)");
            builder.AppendLine("    {");
            builder.AppendLine("        _client = client ?? throw new global::System.ArgumentNullException(nameof(client));");
            builder.AppendLine("        _pluginId = pluginId ?? throw new global::System.ArgumentNullException(nameof(pluginId));");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.Append("    public static ").Append(clientName)
                .AppendLine(" Create(global::DotBoxD.Abstractions.IServerExtensionWireClient client, string pluginId)");
            builder.Append("        => new ").Append(clientName).AppendLine("(client, pluginId);");
            builder.AppendLine();
            AppendServiceMethod(builder);
            builder.Append(_conv.Helpers);
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static string AccessibilityKeyword(INamedTypeSymbol type)
            => type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";

        private void AppendServiceMethod(StringBuilder builder)
        {
            var locals = new RpcGeneratedLocalNames(_serviceMethod);
            var arguments = locals.Next("__arguments");
            var request = locals.Next("__request");
            var response = locals.Next("__response");
            var result = locals.Next("__result");
            var payloadParameterCount = RpcKernelClientParameters.PayloadParameterCount(_serviceMethod);

            builder.Append("    public ");
            if (_returnShape != ReturnShape.Direct)
            {
                builder.Append("async ");
            }

            builder.Append(TypeName(_serviceMethod.ReturnType)).Append(' ')
                .Append(Identifier(_serviceMethod.Name)).Append('(')
                .Append(ParameterList(_serviceMethod)).AppendLine(")");
            builder.AppendLine("    {");
            builder.Append("        var ").Append(arguments).Append(" = new global::DotBoxD.Plugins.KernelRpcValue[")
                .Append(payloadParameterCount).AppendLine("];");
            for (var i = 0; i < payloadParameterCount; i++)
            {
                var parameter = _serviceMethod.Parameters[i];
                builder.Append("        ").Append(arguments).Append('[').Append(i).Append("] = ")
                    .Append(_conv.WriteExpression(parameter.Type, Identifier(parameter.Name))).AppendLine(";");
            }

            builder.Append("        var ").Append(request)
                .Append(" = global::DotBoxD.Plugins.KernelRpcBinaryCodec.EncodeArguments(")
                .Append(arguments).AppendLine(");");
            if (_returnShape == ReturnShape.Direct)
            {
                builder.Append("        var ").Append(response)
                    .Append(" = _client.InvokeServerExtensionAsync(_pluginId, ")
                    .Append(request).Append(", ")
                    .Append(RpcKernelClientParameters.CancellationTokenArgument(_serviceMethod))
                    .AppendLine(").AsTask().GetAwaiter().GetResult();");
            }
            else
            {
                builder.Append("        var ").Append(response)
                    .Append(" = await _client.InvokeServerExtensionAsync(_pluginId, ")
                    .Append(request).Append(", ")
                    .Append(RpcKernelClientParameters.CancellationTokenArgument(_serviceMethod))
                    .AppendLine(").ConfigureAwait(false);");
            }

            if (_payloadReturnType is null)
            {
                builder.Append("        global::DotBoxD.Plugins.KernelRpcBinaryCodec.DecodeValue(")
                    .Append(response)
                    .AppendLine(").RequireKind(global::DotBoxD.Plugins.KernelRpcValueKind.Unit);");
                builder.AppendLine("        return;");
            }
            else
            {
                builder.Append("        var ").Append(result)
                    .Append(" = global::DotBoxD.Plugins.KernelRpcBinaryCodec.DecodeValue(")
                    .Append(response).AppendLine(");");
                builder.Append("        return ").Append(_conv.ReadExpression(_payloadReturnType, result)).AppendLine(";");
            }

            builder.AppendLine("    }");
            builder.AppendLine();
        }

        private static ReturnShape Shape(ITypeSymbol type, out ITypeSymbol? payloadType)
        {
            if (type.SpecialType == SpecialType.System_Void)
            {
                payloadType = null;
                return ReturnShape.Direct;
            }

            if (DotBoxDRpcReturnType.PayloadType(type) is null)
            {
                payloadType = null;
                return type is INamedTypeSymbol { Name: "ValueTask" }
                    ? ReturnShape.ValueTask
                    : ReturnShape.Task;
            }

            if (IsGenericTask(type, out var taskPayloadType))
            {
                payloadType = taskPayloadType;
                return ReturnShape.Task;
            }

            if (IsGenericValueTask(type, out var valueTaskPayloadType))
            {
                payloadType = valueTaskPayloadType;
                return ReturnShape.ValueTask;
            }

            payloadType = type;
            return ReturnShape.Direct;
        }

        private static string ParameterList(IMethodSymbol method)
        {
            var parts = new List<string>();
            foreach (var parameter in method.Parameters)
            {
                parts.Add(TypeName(parameter.Type) + " " + Identifier(parameter.Name));
            }

            return string.Join(", ", parts);
        }

        private static string TypeName(ITypeSymbol type)
            => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        private static string Identifier(string name) => "@" + name;
    }
}
