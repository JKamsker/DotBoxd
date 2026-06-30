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

        if (type.IsGenericType || type.TypeParameters.Length > 0)
        {
            return Fail(declaration, $"Generated server extension '{type.Name}' cannot be generic.");
        }

        if (type.ContainingType is not null)
        {
            return Fail(declaration, $"Server extension kernels must be top-level types; '{type.ToDisplayString()}' is nested.");
        }

        var serviceType = ServiceType(context.Attributes);
        var graftType = GraftType(context.Attributes);

        try
        {
            var graft = RpcServerExtensionGraft.Create(type, graftType);
            var method = ResolveBatchMethod(type, context.SemanticModel.Compilation);
            ValidateBatchMethodParameters(method);
            var liveSettings = PluginSymbolReader.LiveSettings(type, context.SemanticModel, cancellationToken);
            if (ContainsUnsupported(liveSettings))
            {
                throw new NotSupportedException("Live settings must use supported scalar types.");
            }

            ValidateGeneratedParameterNames(method, liveSettings, graft);
            IMethodSymbol? serviceMethod = null;
            RpcKernelClientExtensions? clientExtensions = null;
            RpcKernelClientMethodExtension? directClientMethod = null;
            if (serviceType is not null)
            {
                serviceMethod = RpcKernelClientProxyEmitter.ResolveServiceMethod(
                    serviceType,
                    method,
                    context.SemanticModel.Compilation);
                clientExtensions = RpcKernelClientExtensionModelFactory.Resolve(type, method);
                RpcKernelClientExtensionModelFactory.ValidateLanguageVersion(
                    clientExtensions,
                    context.SemanticModel.SyntaxTree.Options);
                ValidateGeneratedClientTypeCollisions(type, clientExtensions);
            }
            else if (graft is not null)
            {
                directClientMethod = RpcKernelClientExtensionModelFactory.ResolveClientMethod(method, graft.ReceiverType);
                if (directClientMethod is not null &&
                    !SymbolEqualityComparer.Default.Equals(directClientMethod.ReceiverType, graft.ReceiverType))
                {
                    throw new NotSupportedException(
                        $"Server extension client method receiver '{directClientMethod.ReceiverType.ToDisplayString()}' " +
                        $"must match the class receiver '{graft.ReceiverType.ToDisplayString()}'.");
                }

                if (directClientMethod is not null)
                {
                    ValidateGeneratedTypeCollision(type, type.Name + "DirectServerExtensionClientExtensions");
                }
            }
            else if (RpcKernelClientExtensionModelFactory.HasClientPropertyAttribute(type))
            {
                RejectClientPropertyWithoutService(type);
            }
            else if (RpcKernelClientExtensionModelFactory.HasReceiverExtensionAttribute(method))
            {
                throw new NotSupportedException(
                    "[ServerExtensionMethod] requires a service-backed or receiver-grafted [ServerExtension] class.");
            }
            var body = MethodBody(method, cancellationToken);
            var capabilities = new SortedSet<string>(StringComparer.Ordinal);
            var effects = new SortedSet<string>(StringComparer.Ordinal);
            var contextParameter = method.Parameters[method.Parameters.Length - 1];
            var lowerer = new DotBoxDRpcJsonLowerer(
                context.SemanticModel,
                capabilities,
                effects,
                cancellationToken,
                serverContextParameterName: contextParameter.Name,
                serverContextType: contextParameter.Type);
            var hasReceiverId = RpcKernelReceiverHandleSeeder.TrySeed(lowerer, type, graft);
            var bodyJson = body.Block is { } block
                ? lowerer.LowerBody(block)
                : lowerer.LowerExpressionBody(body.Expression!, method.ReturnsVoid);

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
                directClientMethod,
                graft,
                hasReceiverId,
                context.SemanticModel.Compilation);
            var grafts = RpcKernelGraftSignatureFactory.Create(
                type,
                method,
                serviceMethod,
                clientExtensions,
                directClientMethod,
                graft);
            return new RpcKernelModelResult(source, null, grafts);
        }
        catch (NotSupportedException ex)
        {
            return Fail(declaration, ex.Message);
        }
    }

    private static void RejectClientPropertyWithoutService(INamedTypeSymbol type)
    {
        throw new NotSupportedException(
            $"[ServerExtensionClient] on server extension '{type.ToDisplayString()}' requires a service-backed [ServerExtension(id, serviceType)] class.");
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
        RpcKernelClientMethodExtension? directClientMethod,
        RpcServerExtensionGraft? graft,
        bool hasReceiverId,
        Compilation compilation)
    {
        var methodName = method.Name;
        var returnType = DotBoxDRpcReturnType.JsonType(method.ReturnType, compilation);
        var parameters = new List<string>();
        if (hasReceiverId)
        {
            parameters.Add($"{{\"name\":{Str(RpcKernelReceiverHandleSeeder.ReceiverIdParameter)},\"type\":\"String\"}}");
        }

        for (var i = 0; i < method.Parameters.Length - 1; i++)
        {
            var parameter = method.Parameters[i];
            parameters.Add($"{{\"name\":{Str(parameter.Name)},\"type\":{DotBoxDRpcTypeMapper.JsonType(parameter.Type, compilation)}}}");
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
            BuildSource(
                type,
                json,
                serviceType,
                serviceMethod,
                clientExtensions,
                directClientMethod,
                graft,
                method,
                compilation),
            Namespace(type),
            PackageName(type.Name));
    }

    private static string BuildSource(
        INamedTypeSymbol type,
        string json,
        INamedTypeSymbol? serviceType,
        IMethodSymbol? serviceMethod,
        RpcKernelClientExtensions? clientExtensions,
        RpcKernelClientMethodExtension? directClientMethod,
        RpcServerExtensionGraft? graft,
        IMethodSymbol kernelMethod,
        Compilation compilation)
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
            builder.Append(RpcKernelClientProxyEmitter.Emit(type, serviceType, serviceMethod, compilation));
            if (clientExtensions is { IsEmpty: false })
            {
                builder.AppendLine();
                builder.Append(RpcKernelClientExtensionEmitter.Emit(type, serviceType, serviceMethod, clientExtensions));
            }
        }
        else if (graft is not null &&
                 directClientMethod is not null)
        {
            builder.AppendLine();
            builder.Append(RpcKernelDirectClientExtensionEmitter.Emit(
                type,
                graft,
                kernelMethod,
                directClientMethod,
                compilation));
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
        var ns = Namespace(type);
        return string.IsNullOrEmpty(ns)
            ? packageName + ".g.cs"
            : ns.Replace("@", "") + "." + packageName + ".g.cs";
    }

    private static string Namespace(INamedTypeSymbol type)
        => type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString();

    private static RpcKernelModelResult Fail(ClassDeclarationSyntax declaration, string message)
        => new(null, PluginKernelDiagnostic.Create(declaration.Identifier, message), default);

    private static void ValidateGeneratedClientTypeCollisions(
        INamedTypeSymbol type,
        RpcKernelClientExtensions? clientExtensions)
    {
        ValidateGeneratedTypeCollision(type, type.Name + "ServerExtensionClient");
        if (clientExtensions is { IsEmpty: false })
        {
            ValidateGeneratedTypeCollision(type, type.Name + "ServerExtensionClientExtensions");
        }
    }

    private static void ValidateGeneratedTypeCollision(INamedTypeSymbol type, string generatedName)
    {
        foreach (var existing in type.ContainingNamespace.GetTypeMembers(generatedName))
        {
            if (!SymbolEqualityComparer.Default.Equals(existing, type))
            {
                throw new NotSupportedException(
                    $"Generated server extension type '{generatedName}' collides with an existing type in namespace '{type.ContainingNamespace.ToDisplayString()}'.");
            }
        }
    }
}

internal sealed record RpcKernelModelResult(
    GeneratedPluginPackage? Package,
    PluginKernelDiagnostic? Diagnostic,
    EquatableArray<RpcKernelGraftSignature> Grafts);
