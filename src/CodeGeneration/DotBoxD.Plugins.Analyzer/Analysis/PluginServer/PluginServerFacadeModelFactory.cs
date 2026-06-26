using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DotBoxD.Plugins.Analyzer.Analysis.PluginServer.PluginServerFacadeNameFormatter;
namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static partial class PluginServerFacadeModelFactory
{
    private const string ServiceControlType = "DotBoxD.Abstractions.IServiceControl";
    private const string ExtensibleControlType = "DotBoxD.Abstractions.IExtensibleControl";
    public static PluginServerFacadeResult? Create(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TargetSymbol is not INamedTypeSymbol type ||
            context.TargetNode is not ClassDeclarationSyntax declaration)
        {
            return null;
        }
        try
        {
            var model = CreateModel(type, context.SemanticModel.Compilation, cancellationToken);
            return new PluginServerFacadeResult(PluginServerFacadeEmitter.Emit(model), null);
        }
        catch (NotSupportedException ex)
        {
            return new PluginServerFacadeResult(null, PluginKernelDiagnostic.Create(declaration.Identifier, ex.Message));
        }
    }
    private static PluginServerFacadeModel CreateModel(
        INamedTypeSymbol type,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var worldType = ResolveWorldType(type)
            ?? throw new NotSupportedException(
                $"Generated plugin server '{type.Name}' must directly implement one [DotBoxDService] world interface.");
        var controlServiceType = ResolveControlService(type, compilation, worldType)
            ?? throw new NotSupportedException(
                $"Generated plugin server '{type.Name}' could not resolve a control-plane contract. Set ControlService = typeof(TControlService), or declare {worldType.ContainingNamespace}.Ipc.IGamePluginControlService.");
        var liveSettingUpdateType = ResolveLiveSettingUpdateType(controlServiceType, cancellationToken)
            ?? throw new NotSupportedException(
                $"Generated plugin server '{type.Name}' control-plane contract '{controlServiceType.ToDisplayString()}' must declare UpdateSettingsAsync with a typed array parameter carrying the live-setting updates.");
        ValidateControlServiceContract(type, compilation, controlServiceType, liveSettingUpdateType);
        ValidatePublicFacadeSignatureTypes(type, worldType, controlServiceType, liveSettingUpdateType);
        var controls = ResolveControls(worldType, cancellationToken);
        var worldServiceWrappers = new Dictionary<string, ServiceWrapperBuilder>(StringComparer.Ordinal);
        var worldProperties = ResolveForwardedProperties(
            worldType,
            worldServiceWrappers,
            skipServiceProperties: true,
            cancellationToken);
        var worldMethods = ResolveMethods(
            worldType,
            worldServiceWrappers,
            cancellationToken);
        ValidateGeneratedSurfaceCollisions(worldType, worldProperties, worldMethods, controls);
        var ns = type.ContainingNamespace.IsGlobalNamespace ? string.Empty : type.ContainingNamespace.ToDisplayString();
        var eventCallback = PluginServerEventCallbackResolver.Resolve(compilation, worldType, cancellationToken);
        var context = ResolveContext(type, compilation, cancellationToken);
        return new PluginServerFacadeModel(
            ns,
            AccessibilityText(type.DeclaredAccessibility),
            type.Name,
            ServerInterfaceName(worldType),
            SetupInterfaceName(type.Name),
            context.Namespace,
            AccessibilityText(context.Type.DeclaredAccessibility),
            context.Type.Name,
            TypeName(context.Type),
            context.FactoryMethodName,
            new EquatableArray<GeneratedKernelMethodDescriptorModel>(
                GeneratedKernelMethodDescriptorFactory.Create(context.Type, worldType, compilation, cancellationToken)),
            HookRegistryName(type.Name),
            SubscriptionRegistryName(type.Name),
            TypeName(worldType),
            PluginServerWorldExtensionSuffixResolver.Resolve(compilation, worldType, cancellationToken),
            PluginServerXmlDocumentation.FromSymbol(
                worldType,
                "Generated plugin-side facade for the remote world domain.",
                cancellationToken),
            TypeName(controlServiceType),
            TypeName(liveSettingUpdateType),
            new EquatableArray<PluginServerForwardedProperty>(worldProperties),
            new EquatableArray<PluginServerForwardedMethod>(worldMethods),
            new EquatableArray<PluginServerControlProperty>(controls),
            eventCallback is null ? null : TypeName(eventCallback.Value.Type),
            eventCallback?.ProvideSuffix,
            eventCallback is null ? null : TypeName(eventCallback.Value.ReturnType),
            eventCallback?.ReturnHasValue ?? false);
    }

    private static PluginServerForwardedMethod[] ResolveMethods(
        INamedTypeSymbol controlType,
        Dictionary<string, ServiceWrapperBuilder> serviceWrappers,
        CancellationToken cancellationToken)
    {
        var methods = new List<PluginServerForwardedMethod>();
        foreach (var member in MembersIncludingInherited(controlType))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } method &&
                !IsControlPlaneMember(method.ContainingType))
            {
                ValidateForwardedMethod(method);
                var (returnWrapperName, returnWrapperKind) = ResolveReturnWrapper(
                    method.ReturnType,
                    serviceWrappers,
                    cancellationToken);
                methods.Add(new PluginServerForwardedMethod(
                    method.Name,
                    TypeName(method.ReturnType),
                    PluginServerXmlDocumentation.FromSymbol(
                        method,
                        "Forwards " + method.Name + " to the remote domain service.",
                        cancellationToken),
                    returnWrapperName,
                    returnWrapperKind,
                    new EquatableArray<PluginServerParameter>(ResolveParameters(method))));
            }
        }
        return methods.ToArray();
    }

    private static void ValidateForwardedMethod(IMethodSymbol method)
    {
        if (method.IsStatic)
        {
            throw new NotSupportedException(
                $"Generated plugin server member '{method.ToDisplayString()}' must be an instance method.");
        }

        if (method.IsGenericMethod)
        {
            throw new NotSupportedException(
                $"Generated plugin server member '{method.ToDisplayString()}' must not be generic.");
        }

        foreach (var parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
            {
                throw new NotSupportedException(
                    $"Generated plugin server member '{method.ToDisplayString()}' must not declare ref, out, or in parameters.");
            }
        }
    }
    private static string EnsureServiceWrapper(
        INamedTypeSymbol serviceType,
        Dictionary<string, ServiceWrapperBuilder> serviceWrappers,
        CancellationToken cancellationToken)
    {
        var typeName = TypeName(serviceType);
        if (serviceWrappers.TryGetValue(typeName, out var existing))
        {
            return existing.WrapperName;
        }
        var wrapper = new ServiceWrapperBuilder(
            typeName,
            UniqueServiceWrapperName(serviceType, serviceWrappers.Values.Select(w => w.WrapperName)),
            PluginServerXmlDocumentation.FromSymbol(
                serviceType,
                "Generated scoped client for the remote " + serviceType.Name + " domain service.",
                cancellationToken));
        serviceWrappers.Add(typeName, wrapper);
        PopulateServiceWrapper(serviceType, wrapper, serviceWrappers, cancellationToken);
        return wrapper.WrapperName;
    }
    private static (string? Name, PluginServerReturnWrapperKind Kind) ResolveReturnWrapper(
        ITypeSymbol returnType,
        Dictionary<string, ServiceWrapperBuilder> serviceWrappers,
        CancellationToken cancellationToken)
    {
        var wrapperKind = PluginServerReturnWrapperKind.Sync;
        var serviceType = returnType;
        if (DotBoxDTypeNameReader.TryUnwrapTaskLike(returnType, out var unwrapped))
        {
            serviceType = unwrapped;
            wrapperKind = returnType is INamedTypeSymbol { Name: "Task" }
                ? PluginServerReturnWrapperKind.Task
                : PluginServerReturnWrapperKind.ValueTask;
        }
        if (serviceType is not INamedTypeSymbol namedServiceType ||
            !HasAttribute(namedServiceType, DotBoxDMetadataNames.DotBoxDServiceAttribute))
        {
            return (null, PluginServerReturnWrapperKind.None);
        }
        return (EnsureServiceWrapper(namedServiceType, serviceWrappers, cancellationToken), wrapperKind);
    }
    private static void PopulateServiceWrapper(
        INamedTypeSymbol serviceType,
        ServiceWrapperBuilder wrapper,
        Dictionary<string, ServiceWrapperBuilder> serviceWrappers,
        CancellationToken cancellationToken)
    {
        wrapper.Properties.AddRange(ResolveForwardedProperties(
            serviceType,
            serviceWrappers,
            skipServiceProperties: false,
            cancellationToken));
        wrapper.Methods.AddRange(ResolveMethods(serviceType, serviceWrappers, cancellationToken));
    }
    private static PluginServerParameter[] ResolveParameters(IMethodSymbol method)
    {
        var parameters = new PluginServerParameter[method.Parameters.Length];
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];
            parameters[i] = new PluginServerParameter(parameter.Name, TypeName(parameter.Type));
        }

        return parameters;
    }
    private static bool HasAttribute(INamedTypeSymbol type, string metadataName)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (string.Equals(attribute.AttributeClass?.ToDisplayString(), metadataName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
    private static bool IsControlPlaneMember(INamedTypeSymbol type)
    {
        var name = type.ToDisplayString();
        return string.Equals(name, ServiceControlType, StringComparison.Ordinal) ||
               string.Equals(name, ExtensibleControlType, StringComparison.Ordinal);
    }
    private static IEnumerable<ISymbol> MembersIncludingInherited(INamedTypeSymbol type)
    {
        foreach (var inherited in type.AllInterfaces.Reverse())
        {
            foreach (var member in inherited.GetMembers())
            {
                yield return member;
            }
        }

        foreach (var member in type.GetMembers())
        {
            yield return member;
        }
    }
    private sealed class ServiceWrapperBuilder(string type, string wrapperName, string documentation)
    {
        public string Type { get; } = type;
        public string WrapperName { get; } = wrapperName;
        public string Documentation { get; } = documentation;
        public List<PluginServerForwardedProperty> Properties { get; } = [];
        public List<PluginServerForwardedMethod> Methods { get; } = [];
    }
}
