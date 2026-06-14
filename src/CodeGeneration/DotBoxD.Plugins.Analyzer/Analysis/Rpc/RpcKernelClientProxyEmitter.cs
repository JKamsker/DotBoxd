namespace DotBoxD.Plugins.Analyzer;

using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

internal static partial class RpcKernelClientProxyEmitter
{
    public static string Emit(
        INamedTypeSymbol kernelType,
        IMethodSymbol kernelMethod,
        INamedTypeSymbol serviceType)
    {
        if (serviceType.TypeKind != TypeKind.Interface)
        {
            throw new NotSupportedException("Kernel RPC service client generation requires an interface contract type.");
        }

        var serviceMethod = ResolveServiceMethod(serviceType, kernelMethod);
        return new ProxySourceWriter(kernelType, serviceType, serviceMethod).Emit();
    }

    private static IMethodSymbol ResolveServiceMethod(INamedTypeSymbol serviceType, IMethodSymbol kernelMethod)
    {
        var methods = new List<IMethodSymbol>();
        foreach (var member in serviceType.GetMembers())
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary, IsStatic: false } method)
            {
                methods.Add(method);
            }
        }

        if (methods.Count != 1)
        {
            throw new NotSupportedException(
                $"Kernel RPC service interface '{serviceType.ToDisplayString()}' must declare exactly one method.");
        }

        var serviceMethod = methods[0];
        var expectedName = kernelMethod.Name;
        if (!string.Equals(serviceMethod.Name, expectedName, StringComparison.Ordinal) &&
            !string.Equals(serviceMethod.Name, expectedName + "Async", StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Kernel RPC service method '{serviceMethod.Name}' must match kernel method '{expectedName}' or '{expectedName}Async'.");
        }

        var kernelParameterCount = kernelMethod.Parameters.Length - 1;
        if (serviceMethod.Parameters.Length != kernelParameterCount)
        {
            throw new NotSupportedException(
                $"Kernel RPC service method '{serviceMethod.Name}' must declare {kernelParameterCount} parameter(s).");
        }

        for (var i = 0; i < kernelParameterCount; i++)
        {
            if (!SymbolEqualityComparer.Default.Equals(serviceMethod.Parameters[i].Type, kernelMethod.Parameters[i].Type))
            {
                throw new NotSupportedException(
                    $"Kernel RPC service parameter '{serviceMethod.Parameters[i].Name}' must match kernel parameter '{kernelMethod.Parameters[i].Name}'.");
            }
        }

        if (!SymbolEqualityComparer.Default.Equals(UnwrapReturn(serviceMethod.ReturnType), kernelMethod.ReturnType))
        {
            throw new NotSupportedException(
                $"Kernel RPC service method '{serviceMethod.Name}' return type must match kernel method '{kernelMethod.Name}'.");
        }

        return serviceMethod;
    }

    private static ITypeSymbol UnwrapReturn(ITypeSymbol type)
        => IsGenericTask(type, out var inner) || IsGenericValueTask(type, out inner) ? inner : type;

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

    private sealed partial class ProxySourceWriter
    {
        private readonly INamedTypeSymbol _kernelType;
        private readonly INamedTypeSymbol _serviceType;
        private readonly IMethodSymbol _serviceMethod;
        private readonly ITypeSymbol _payloadReturnType;
        private readonly ReturnShape _returnShape;
        private readonly StringBuilder _helpers = new();
        private readonly Dictionary<string, string> _readers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _writers = new(StringComparer.Ordinal);
        private int _nextHelper;

        public ProxySourceWriter(
            INamedTypeSymbol kernelType,
            INamedTypeSymbol serviceType,
            IMethodSymbol serviceMethod)
        {
            _kernelType = kernelType;
            _serviceType = serviceType;
            _serviceMethod = serviceMethod;
            _returnShape = Shape(serviceMethod.ReturnType, out _payloadReturnType);
        }

        public string Emit()
        {
            var builder = new StringBuilder();
            var clientName = _kernelType.Name + "RpcClient";
            builder.Append("public sealed class ").Append(clientName).Append(" : ").AppendLine(TypeName(_serviceType));
            builder.AppendLine("{");
            builder.AppendLine("    private readonly global::DotBoxD.Plugins.IKernelRpcWireClient _client;");
            builder.AppendLine("    private readonly string _pluginId;");
            builder.AppendLine();
            builder.Append("    public ").Append(clientName)
                .AppendLine("(global::DotBoxD.Plugins.IKernelRpcWireClient client, string pluginId)");
            builder.AppendLine("    {");
            builder.AppendLine("        _client = client ?? throw new global::System.ArgumentNullException(nameof(client));");
            builder.AppendLine("        _pluginId = pluginId ?? throw new global::System.ArgumentNullException(nameof(pluginId));");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.Append("    public static ").Append(TypeName(_serviceType))
                .AppendLine(" Create(global::DotBoxD.Plugins.IKernelRpcWireClient client, string pluginId)");
            builder.Append("        => new ").Append(clientName).AppendLine("(client, pluginId);");
            builder.AppendLine();
            AppendServiceMethod(builder);
            builder.Append(_helpers);
            builder.AppendLine("}");
            return builder.ToString();
        }

        private void AppendServiceMethod(StringBuilder builder)
        {
            builder.Append("    public ");
            if (_returnShape != ReturnShape.Direct)
            {
                builder.Append("async ");
            }

            builder.Append(TypeName(_serviceMethod.ReturnType)).Append(' ')
                .Append(Identifier(_serviceMethod.Name)).Append('(')
                .Append(ParameterList(_serviceMethod)).AppendLine(")");
            builder.AppendLine("    {");
            builder.Append("        var __arguments = new global::DotBoxD.Plugins.KernelRpcValue[")
                .Append(_serviceMethod.Parameters.Length).AppendLine("];");
            for (var i = 0; i < _serviceMethod.Parameters.Length; i++)
            {
                var parameter = _serviceMethod.Parameters[i];
                builder.Append("        __arguments[").Append(i).Append("] = ")
                    .Append(WriteExpression(parameter.Type, Identifier(parameter.Name))).AppendLine(";");
            }

            builder.AppendLine("        var __request = global::DotBoxD.Plugins.KernelRpcBinaryCodec.EncodeArguments(__arguments);");
            if (_returnShape == ReturnShape.Direct)
            {
                builder.AppendLine("        var __response = _client.InvokeKernelRpcAsync(_pluginId, __request).AsTask().GetAwaiter().GetResult();");
            }
            else
            {
                builder.AppendLine("        var __response = await _client.InvokeKernelRpcAsync(_pluginId, __request).ConfigureAwait(false);");
            }

            builder.AppendLine("        var __result = global::DotBoxD.Plugins.KernelRpcBinaryCodec.DecodeValue(__response);");
            builder.Append("        return ").Append(ReadExpression(_payloadReturnType, "__result")).AppendLine(";");
            builder.AppendLine("    }");
            builder.AppendLine();
        }

        private static ReturnShape Shape(ITypeSymbol type, out ITypeSymbol payloadType)
        {
            if (IsGenericTask(type, out payloadType))
            {
                return ReturnShape.Task;
            }

            if (IsGenericValueTask(type, out payloadType))
            {
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

        private string NextHelperName(string prefix) => prefix + "KernelRpcValue" + _nextHelper++;

        private static string TypeName(ITypeSymbol type)
            => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        private static string TypeKey(ITypeSymbol type)
            => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        private static string Identifier(string name) => "@" + name;
    }
}
