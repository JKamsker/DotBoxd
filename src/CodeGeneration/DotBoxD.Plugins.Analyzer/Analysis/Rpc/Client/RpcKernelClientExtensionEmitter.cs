namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

internal sealed record RpcKernelClientPropertyExtension(
    INamedTypeSymbol ReceiverType,
    string Name,
    INamedTypeSymbol? ServerExtensionsInterfaceType);

internal sealed record RpcKernelClientMethodExtension(
    INamedTypeSymbol ReceiverType,
    string Name,
    INamedTypeSymbol? ServerExtensionsInterfaceType);

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
        const string receiver = "value";

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
        AppendClientExpression(
            builder,
            kernelType,
            serviceType,
            receiver,
            property.ServerExtensionsInterfaceType);
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
            .Append(Identifier(method.Name)).Append('(').Append(ParameterList(serviceMethod)).AppendLine(")");
        AppendClientExpression(
            builder,
            kernelType,
            serviceType,
            receiver,
            method.ServerExtensionsInterfaceType,
            serviceMethod);
    }

    private static void AppendClientExpression(
        StringBuilder builder,
        INamedTypeSymbol kernelType,
        INamedTypeSymbol serviceType,
        string receiver,
        INamedTypeSymbol? serverExtensionsInterfaceType,
        IMethodSymbol? serviceMethod = null)
    {
        var registry = ServerExtensionsRegistryExpression(receiver, serverExtensionsInterfaceType);
        builder.Append("            => ").Append(ClientTypeName(kernelType)).AppendLine(".Create(");
        builder.Append("                ").Append(registry).AppendLine(",");
        builder.Append("                ").Append(registry).Append(".PluginId<")
            .Append(TypeName(serviceType)).Append(">())");
        if (serviceMethod is null)
        {
            builder.AppendLine(";");
            return;
        }

        builder.AppendLine();
        builder.Append("                .").Append(Identifier(serviceMethod.Name)).Append('(')
            .Append(ArgumentList(serviceMethod)).AppendLine(");");
    }

    private static bool SameReceiver(
        RpcKernelClientPropertyExtension? property,
        RpcKernelClientMethodExtension? method)
        => property is not null &&
           method is not null &&
           SymbolEqualityComparer.Default.Equals(property.ReceiverType, method.ReceiverType);

    private static string ParameterList(IMethodSymbol method)
    {
        var parts = new List<string>();
        foreach (var parameter in method.Parameters)
        {
            parts.Add(TypeName(parameter.Type) + " " + Identifier(parameter.Name));
        }

        return string.Join(", ", parts);
    }

    private static string ArgumentList(IMethodSymbol method)
    {
        var parts = new List<string>();
        foreach (var parameter in method.Parameters)
        {
            parts.Add(Identifier(parameter.Name));
        }

        return string.Join(", ", parts);
    }

    private static string ClientTypeName(INamedTypeSymbol kernelType)
    {
        var ns = kernelType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : kernelType.ContainingNamespace.ToDisplayString() + ".";
        return "global::" + ns + kernelType.Name + "ServerExtensionClient";
    }

    private static string ServerExtensionsRegistryExpression(string receiver, INamedTypeSymbol? interfaceType)
        => interfaceType is null
            ? receiver + ".ServerExtensions"
            : "((" + TypeName(interfaceType) + ")" + receiver + ").ServerExtensions";

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Identifier(string name) => "@" + name;
}
