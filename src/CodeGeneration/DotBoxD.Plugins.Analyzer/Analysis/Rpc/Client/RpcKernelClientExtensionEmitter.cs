namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Text;
using Microsoft.CodeAnalysis;

internal sealed record RpcKernelClientPropertyExtension(
    INamedTypeSymbol ReceiverType,
    string Name);

internal sealed record RpcKernelClientMethodExtension(
    INamedTypeSymbol ReceiverType,
    string Name);

internal sealed record RpcKernelClientExtensions(
    RpcKernelClientPropertyExtension? Property,
    RpcKernelClientMethodExtension? Method)
{
    public bool IsEmpty => Property is null && Method is null;
}

internal static class RpcKernelClientExtensionEmitter
{
    public static string Emit(
        INamedTypeSymbol kernelType,
        INamedTypeSymbol serviceType,
        IMethodSymbol serviceMethod,
        RpcKernelClientExtensions extensions)
    {
        if (extensions.IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append("internal static class ").Append(kernelType.Name).AppendLine("ServerExtensionClientExtensions");
        builder.AppendLine("{");

        if (extensions.Property is { } property)
        {
            var method = SameReceiver(property, extensions.Method) ? extensions.Method : null;
            AppendBlock(builder, kernelType, serviceType, serviceMethod, property.ReceiverType, property, method);
        }

        if (extensions.Method is { } methodExtension &&
            !SameReceiver(extensions.Property, methodExtension))
        {
            AppendBlock(builder, kernelType, serviceType, serviceMethod, methodExtension.ReceiverType, null, methodExtension);
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendBlock(
        StringBuilder builder,
        INamedTypeSymbol kernelType,
        INamedTypeSymbol serviceType,
        IMethodSymbol serviceMethod,
        INamedTypeSymbol receiverType,
        RpcKernelClientPropertyExtension? property,
        RpcKernelClientMethodExtension? method)
    {
        var receiver = ReceiverParameterName(serviceMethod);

        builder.Append("    extension(").Append(TypeName(receiverType)).Append(' ').Append(receiver).AppendLine(")");
        builder.AppendLine("    {");
        if (property is not null)
        {
            AppendProperty(builder, kernelType, serviceType, property, receiver);
        }

        if (method is not null)
        {
            if (property is not null)
            {
                builder.AppendLine();
            }

            AppendMethod(builder, kernelType, serviceType, serviceMethod, method, receiver);
        }

        builder.AppendLine("    }");
    }

    private static void AppendProperty(
        StringBuilder builder,
        INamedTypeSymbol kernelType,
        INamedTypeSymbol serviceType,
        RpcKernelClientPropertyExtension property,
        string receiver)
    {
        builder.Append("        public ").Append(TypeName(serviceType)).Append(' ')
            .Append(Identifier(property.Name)).AppendLine();
        builder.AppendLine("        {");
        builder.AppendLine("            get");
        builder.AppendLine("            {");
        AppendAccessorGuard(builder, receiver, "                ");
        AppendClientReturn(
            builder,
            kernelType,
            serviceType,
            serviceMethod: null,
            indent: "                ");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
    }

    private static void AppendMethod(
        StringBuilder builder,
        INamedTypeSymbol kernelType,
        INamedTypeSymbol serviceType,
        IMethodSymbol serviceMethod,
        RpcKernelClientMethodExtension method,
        string receiver)
    {
        builder.Append("        public ").Append(TypeName(serviceMethod.ReturnType)).Append(' ')
            .Append(Identifier(method.Name)).Append('(')
            .Append(RpcKernelClientParameterSource.ParameterList(serviceMethod)).AppendLine(")");
        builder.AppendLine("        {");
        AppendAccessorGuard(builder, receiver, "            ");
        AppendClientReturn(
            builder,
            kernelType,
            serviceType,
            serviceMethod,
            indent: "            ");
        builder.AppendLine("        }");
    }

    private static void AppendAccessorGuard(StringBuilder builder, string receiver, string indent)
    {
        builder.Append(indent).Append("if (").Append(receiver)
            .AppendLine(" is not global::DotBoxD.Abstractions.IServerExtensionClientAccessor __accessor)");
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).AppendLine("    throw new global::System.InvalidOperationException(\"Server extension calls require a generated plugin facade receiver.\");");
        builder.Append(indent).AppendLine("}");
    }

    private static void AppendClientReturn(
        StringBuilder builder,
        INamedTypeSymbol kernelType,
        INamedTypeSymbol serviceType,
        IMethodSymbol? serviceMethod,
        string indent)
    {
        const string registry = "__accessor.ServerExtensions";
        builder.Append(indent).Append("return ").Append(ClientTypeName(kernelType)).AppendLine(".Create(");
        builder.Append(indent).Append("    ").Append(registry).AppendLine(",");
        builder.Append(indent).Append("    ").Append(registry).Append(".PluginId<")
            .Append(TypeName(serviceType)).Append(">())");
        if (serviceMethod is null)
        {
            builder.AppendLine(";");
            return;
        }

        builder.AppendLine();
        builder.Append(indent).Append("    .").Append(Identifier(serviceMethod.Name)).Append('(')
            .Append(RpcKernelClientParameterSource.ArgumentList(serviceMethod)).AppendLine(");");
    }

    private static bool SameReceiver(
        RpcKernelClientPropertyExtension? property,
        RpcKernelClientMethodExtension? method)
        => property is not null &&
           method is not null &&
           SymbolEqualityComparer.Default.Equals(property.ReceiverType, method.ReceiverType);

    private static string ClientTypeName(INamedTypeSymbol kernelType)
    {
        var ns = kernelType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : kernelType.ContainingNamespace.ToDisplayString() + ".";
        return "global::" + ns + kernelType.Name + "ServerExtensionClient";
    }

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Identifier(string name) => "@" + name;

    private static string ReceiverParameterName(IMethodSymbol serviceMethod)
    {
        const string seed = "__receiver";
        if (!HasParameter(serviceMethod, seed))
        {
            return seed;
        }

        for (var suffix = 0; ; suffix++)
        {
            var candidate = seed + "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!HasParameter(serviceMethod, candidate))
            {
                return candidate;
            }
        }
    }

    private static bool HasParameter(IMethodSymbol method, string name)
        => method.Parameters.Any(parameter => string.Equals(parameter.Name, name, StringComparison.Ordinal));
}
