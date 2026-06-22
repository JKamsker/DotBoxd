using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using static DotBoxDRpcJsonLowerer;

/// <summary>
/// Lowers a <c>[ServerExtension]</c> class to a generated <c>&lt;Name&gt;PluginPackage</c> whose
/// <c>Create()</c> imports the verified IR JSON (so it ships exactly like an event kernel and installs
/// via <c>PluginServer.InstallServerExtensionAsync</c>). The class must declare one public batch method whose last
/// parameter is <c>HookContext</c> (the host-binding lowering marker); its block body is lowered by
/// <see cref="DotBoxDRpcJsonLowerer"/>. Unsupported shapes produce a diagnostic and no package.
/// </summary>
internal static partial class RpcKernelModelFactory
{
    public static RpcKernelModelResult? Create(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TargetSymbol is not INamedTypeSymbol type ||
            context.TargetNode is not ClassDeclarationSyntax declaration)
        {
            return null;
        }

        var pluginId = PluginId(context.Attributes, type.Name);
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return Fail(declaration, "Server extension id must be a non-empty string.");
        }

        var serviceType = ServiceType(context.Attributes);
        var graftType = GraftType(context.Attributes);

        try
        {
            var method = ResolveBatchMethod(type);
            ValidateBatchMethodParameters(method);
            var liveSettings = PluginSymbolReader.LiveSettings(type, context.SemanticModel, cancellationToken);
            if (ContainsUnsupported(liveSettings))
            {
                throw new NotSupportedException("Live settings must use supported scalar types.");
            }

            ValidateGeneratedParameterNames(method, liveSettings);
            IMethodSymbol? serviceMethod = null;
            RpcKernelClientExtensions? clientExtensions = null;
            if (serviceType is not null)
            {
                serviceMethod = RpcKernelClientProxyEmitter.ResolveServiceMethod(serviceType, method);
                clientExtensions = RpcKernelClientExtensionModelFactory.Resolve(type, method);
            }
            var body = MethodBody(method, cancellationToken);
            var capabilities = new SortedSet<string>(StringComparer.Ordinal);
            var effects = new SortedSet<string>(StringComparer.Ordinal);
            var lowerer = new DotBoxDRpcJsonLowerer(context.SemanticModel, capabilities, effects, cancellationToken);
            var hasReceiverId = RpcKernelReceiverHandleSeeder.TrySeed(lowerer, type, graftType);
            var bodyJson = lowerer.LowerBody(body);

            effects.Add("Cpu");
            if (lowerer.Allocates)
            {
                effects.Add("Alloc");
            }

            var source = EmitPackage(
                type,
                pluginId!,
                method,
                bodyJson,
                effects,
                capabilities,
                liveSettings,
                serviceType,
                serviceMethod,
                clientExtensions,
                graftType,
                hasReceiverId);
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
                    DotBoxDMetadataNames.HookContextType,
                    StringComparison.Ordinal))
            {
                if (found is not null)
                {
                    throw new NotSupportedException("A server extension must declare exactly one batch method (a public method whose last parameter is HookContext).");
                }

                found = method;
            }
        }

        return found ?? throw new NotSupportedException("A server extension must declare one public batch method whose last parameter is HookContext.");
    }

    private static void ValidateBatchMethodParameters(IMethodSymbol method)
    {
        for (var i = 0; i < method.Parameters.Length - 1; i++)
        {
            var parameter = method.Parameters[i];
            if (parameter.RefKind != RefKind.None)
            {
                throw new NotSupportedException(
                    $"Server extension parameter '{parameter.Name}' cannot use ref, in, or out modifiers.");
            }
        }
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

        throw new NotSupportedException($"Server extension method '{method.Name}' must have a block body declared in source.");
    }

    private static GeneratedPluginPackage EmitPackage(
        INamedTypeSymbol type,
        string pluginId,
        IMethodSymbol method,
        string bodyJson,
        SortedSet<string> effects,
        SortedSet<string> capabilities,
        EquatableArray<LiveSettingModel> liveSettings,
        INamedTypeSymbol? serviceType,
        IMethodSymbol? serviceMethod,
        RpcKernelClientExtensions? clientExtensions,
        INamedTypeSymbol? graftType,
        bool hasReceiverId)
    {
        var methodName = method.Name;
        var returnType = DotBoxDRpcTypeMapper.JsonType(method.ReturnType);
        var parameters = new List<string>();
        if (hasReceiverId)
        {
            parameters.Add($"{{\"name\":{Str(RpcKernelReceiverHandleSeeder.ReceiverIdParameter)},\"type\":\"String\"}}");
        }

        for (var i = 0; i < method.Parameters.Length - 1; i++)
        {
            var parameter = method.Parameters[i];
            parameters.Add($"{{\"name\":{Str(parameter.Name)},\"type\":{DotBoxDRpcTypeMapper.JsonType(parameter.Type)}}}");
        }

        foreach (var setting in liveSettings)
        {
            parameters.Add($"{{\"name\":{Str(setting.Name)},\"type\":{LiveSettingJsonType(setting.Type)}}}");
        }

        var json =
            "{" +
            "\"manifest\":{" +
            $"\"pluginId\":{Str(pluginId)}," +
            $"\"contract\":{Str(type.Name)}," +
            "\"mode\":\"Auto\"," +
            $"\"effects\":[{JoinStrings(effects)}]," +
            $"\"liveSettings\":[{JoinLiveSettings(liveSettings)}]," +
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

        return new GeneratedPluginPackage(
            HintName(type),
            BuildSource(type, json, serviceType, serviceMethod, clientExtensions, graftType, method),
            Namespace(type),
            PackageName(type.Name));
    }

    private static string BuildSource(
        INamedTypeSymbol type,
        string json,
        INamedTypeSymbol? serviceType,
        IMethodSymbol? serviceMethod,
        RpcKernelClientExtensions? clientExtensions,
        INamedTypeSymbol? graftType,
        IMethodSymbol kernelMethod)
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
        builder.Append("    public static ").Append(TypeNames.GlobalPluginPackage).AppendLine(" Create()");
        builder.Append("        => ").Append(TypeNames.GlobalPluginPackageJsonSerializer).Append(".Import(\"")
            .Append(json.Replace("\\", "\\\\").Replace("\"", "\\\""))
            .AppendLine("\");");
        builder.AppendLine("}");
        if (serviceType is not null && serviceMethod is not null)
        {
            builder.AppendLine();
            builder.Append(RpcKernelClientProxyEmitter.Emit(type, serviceType, serviceMethod));
            if (clientExtensions is { IsEmpty: false })
            {
                builder.AppendLine();
                builder.Append(RpcKernelClientExtensionEmitter.Emit(type, serviceType, serviceMethod, clientExtensions));
            }
        }
        else if (graftType is not null &&
                 RpcKernelClientExtensionModelFactory.HasExtensionAttribute(kernelMethod))
        {
            builder.AppendLine();
            builder.Append(RpcKernelDirectClientExtensionEmitter.Emit(type, graftType, kernelMethod));
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

    private static string PluginId(IReadOnlyList<AttributeData> attributes, string kernelName)
    {
        if (attributes.Count > 0)
        {
            var args = attributes[0].ConstructorArguments;
            if (args.Length > 0 && args[0].Value is string id)
            {
                return id;
            }

            if (args.Length > 1 && args[1].Value is string newShapeId)
            {
                return newShapeId;
            }
        }

        return KernelId(kernelName);
    }

    private static INamedTypeSymbol? ServiceType(IReadOnlyList<AttributeData> attributes)
    {
        if (attributes.Count == 0)
        {
            return null;
        }

        var args = attributes[0].ConstructorArguments;
        return args.Length > 1 && args[1].Value is INamedTypeSymbol serviceType
            ? serviceType
            : null;
    }

    private static INamedTypeSymbol? GraftType(IReadOnlyList<AttributeData> attributes)
    {
        if (attributes.Count == 0)
        {
            return null;
        }

        var args = attributes[0].ConstructorArguments;
        return args.Length > 0 && args[0].Value is INamedTypeSymbol graftType
            ? graftType
            : null;
    }

    private static string KernelId(string kernelName)
    {
        var name = kernelName.EndsWith(DotBoxDGenerationNames.KernelSuffix, StringComparison.Ordinal)
            ? kernelName.Substring(0, kernelName.Length - DotBoxDGenerationNames.KernelSuffix.Length)
            : kernelName;
        return ToKebabCase(name);
    }

    private static string ToKebabCase(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsUpper(ch))
            {
                if (builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(ch));
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string HintName(INamedTypeSymbol type)
    {
        var packageName = PackageName(type.Name);
        var ns = Namespace(type);
        return string.IsNullOrEmpty(ns)
            ? packageName + ".g.cs"
            : ns.Replace("@", "") + "." + packageName + ".g.cs";
    }

    private static string Namespace(INamedTypeSymbol type)
        => type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString();

    private static RpcKernelModelResult Fail(ClassDeclarationSyntax declaration, string message)
        => new(null, PluginKernelDiagnostic.Create(declaration.Identifier, message));
}

internal sealed record RpcKernelModelResult(GeneratedPluginPackage? Package, PluginKernelDiagnostic? Diagnostic);
