namespace DotBoxD.Plugins.Analyzer;

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DotBoxDRpcJsonLowerer;

/// <summary>
/// Lowers a <c>[KernelRpcService]</c> class to a generated <c>&lt;Name&gt;PluginPackage</c> whose
/// <c>Create()</c> imports the verified IR JSON (so it ships exactly like an event kernel and installs
/// via <c>PluginServer.InstallRpcAsync</c>). The class must declare one public batch method whose last
/// parameter is <c>HookContext</c> (the host-binding lowering marker); its block body is lowered by
/// <see cref="DotBoxDRpcJsonLowerer"/>. Unsupported shapes produce a diagnostic and no package.
/// </summary>
internal static class RpcKernelModelFactory
{
    public static RpcKernelModelResult? Create(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TargetSymbol is not INamedTypeSymbol type ||
            context.TargetNode is not ClassDeclarationSyntax declaration)
        {
            return null;
        }

        var pluginId = context.Attributes.Length > 0 && context.Attributes[0].ConstructorArguments.Length > 0
            ? context.Attributes[0].ConstructorArguments[0].Value as string
            : null;
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return Fail(declaration, "Kernel RPC service id must be a non-empty string.");
        }

        var serviceType = context.Attributes.Length > 0 && context.Attributes[0].ConstructorArguments.Length > 1
            ? context.Attributes[0].ConstructorArguments[1].Value as INamedTypeSymbol
            : null;

        try
        {
            var method = ResolveBatchMethod(type);
            var body = MethodBody(method, cancellationToken);
            var capabilities = new SortedSet<string>(StringComparer.Ordinal);
            var effects = new SortedSet<string>(StringComparer.Ordinal);
            var lowerer = new DotBoxDRpcJsonLowerer(context.SemanticModel, capabilities, effects, cancellationToken);
            var bodyJson = lowerer.LowerBody(body);

            effects.Add("Cpu");
            if (lowerer.Allocates)
            {
                effects.Add("Alloc");
            }

            var source = EmitPackage(type, pluginId!, method, bodyJson, effects, capabilities, serviceType);
            return new RpcKernelModelResult(source, null);
        }
        catch (NotSupportedException ex)
        {
            return Fail(declaration, ex.Message);
        }
    }

    private static IMethodSymbol ResolveBatchMethod(INamedTypeSymbol type)
    {
        IMethodSymbol? found = null;
        foreach (var member in type.GetMembers())
        {
            if (member is IMethodSymbol
                {
                    MethodKind: MethodKind.Ordinary,
                    DeclaredAccessibility: Accessibility.Public,
                    IsStatic: false
                } method &&
                method.Parameters.Length > 0 &&
                string.Equals(
                    method.Parameters[method.Parameters.Length - 1].Type.ToDisplayString(),
                    DotBoxDGenerationNames.Metadata.HookContextType,
                    StringComparison.Ordinal))
            {
                if (found is not null)
                {
                    throw new NotSupportedException("A kernel RPC service must declare exactly one batch method (a public method whose last parameter is HookContext).");
                }

                found = method;
            }
        }

        return found ?? throw new NotSupportedException("A kernel RPC service must declare one public batch method whose last parameter is HookContext.");
    }

    private static BlockSyntax MethodBody(IMethodSymbol method, CancellationToken cancellationToken)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is MethodDeclarationSyntax { Body: { } block })
            {
                return block;
            }
        }

        throw new NotSupportedException($"Kernel RPC method '{method.Name}' must have a block body declared in source.");
    }

    private static GeneratedPluginPackage EmitPackage(
        INamedTypeSymbol type,
        string pluginId,
        IMethodSymbol method,
        string bodyJson,
        SortedSet<string> effects,
        SortedSet<string> capabilities,
        INamedTypeSymbol? serviceType)
    {
        var methodName = method.Name;
        var returnType = DotBoxDRpcTypeMapper.JsonType(method.ReturnType);
        var parameters = new List<string>();
        for (var i = 0; i < method.Parameters.Length - 1; i++)
        {
            var parameter = method.Parameters[i];
            parameters.Add($"{{\"name\":{Str(parameter.Name)},\"type\":{DotBoxDRpcTypeMapper.JsonType(parameter.Type)}}}");
        }

        var json =
            "{" +
            "\"manifest\":{" +
            $"\"pluginId\":{Str(pluginId)}," +
            $"\"contract\":{Str(type.Name)}," +
            "\"mode\":\"Auto\"," +
            $"\"effects\":[{JoinStrings(effects)}]," +
            "\"liveSettings\":[]," +
            "\"subscriptions\":[]," +
            $"\"requiredCapabilities\":[{JoinStrings(capabilities)}]," +
            $"\"rpcEntrypoint\":{Str(methodName)}}}," +
            $"\"entrypoints\":{{\"shouldHandle\":{Str(methodName)},\"handle\":{Str(methodName)}}}," +
            "\"module\":{" +
            $"\"id\":{Str(pluginId)},\"version\":\"1.0.0\",\"targetSandboxVersion\":\"1.0.0\"," +
            "\"capabilityRequests\":[]," +
            $"\"metadata\":{{\"kernel\":{Str(type.Name)},\"pluginId\":{Str(pluginId)}}}," +
            "\"functions\":[{" +
            $"\"id\":{Str(methodName)},\"visibility\":\"entrypoint\"," +
            $"\"parameters\":[{string.Join(",", parameters)}]," +
            $"\"returnType\":{returnType}," +
            $"\"body\":{bodyJson}}}]}}}}";

        return new GeneratedPluginPackage(HintName(type), BuildSource(type, json, method, serviceType));
    }

    private static string BuildSource(
        INamedTypeSymbol type,
        string json,
        IMethodSymbol method,
        INamedTypeSymbol? serviceType)
    {
        var ns = type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString();
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        if (!string.IsNullOrEmpty(ns))
        {
            builder.Append("namespace ").Append(ns).AppendLine(";");
            builder.AppendLine();
        }

        builder.Append("public static class ").AppendLine(PackageName(type.Name));
        builder.AppendLine("{");
        builder.AppendLine("    public static global::DotBoxD.Plugins.PluginPackage Create()");
        builder.Append("        => global::DotBoxD.Plugins.PluginPackageJsonSerializer.Import(\"")
            .Append(json.Replace("\\", "\\\\").Replace("\"", "\\\""))
            .AppendLine("\");");
        builder.AppendLine("}");
        if (serviceType is not null)
        {
            builder.AppendLine();
            builder.Append(RpcKernelClientProxyEmitter.Emit(type, method, serviceType));
        }

        return builder.ToString();
    }

    private static string JoinStrings(IEnumerable<string> values)
    {
        var parts = new List<string>();
        foreach (var value in values)
        {
            parts.Add(Str(value));
        }

        return string.Join(",", parts);
    }

    private static string PackageName(string kernelName)
        => (kernelName.EndsWith(DotBoxDGenerationNames.KernelSuffix, StringComparison.Ordinal)
            ? kernelName.Substring(0, kernelName.Length - DotBoxDGenerationNames.KernelSuffix.Length)
            : kernelName) + DotBoxDGenerationNames.PluginPackageSuffix;

    private static string HintName(INamedTypeSymbol type)
    {
        var packageName = PackageName(type.Name);
        return type.ContainingNamespace.IsGlobalNamespace
            ? packageName + ".g.cs"
            : type.ContainingNamespace.ToDisplayString().Replace("@", "") + "." + packageName + ".g.cs";
    }

    private static RpcKernelModelResult Fail(ClassDeclarationSyntax declaration, string message)
        => new(null, PluginKernelDiagnostic.Create(declaration.Identifier, message));
}

internal sealed record RpcKernelModelResult(GeneratedPluginPackage? Package, PluginKernelDiagnostic? Diagnostic);
