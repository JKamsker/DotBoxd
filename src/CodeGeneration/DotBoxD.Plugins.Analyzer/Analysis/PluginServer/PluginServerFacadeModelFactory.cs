using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DotBoxD.Plugins.Analyzer.Analysis.PluginServer.PluginServerFacadeNameFormatter;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerFacadeModelFactory
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
        var controlServiceType = ResolveControlService(compilation, worldType)
            ?? throw new NotSupportedException(
                $"Generated plugin server '{type.Name}' could not resolve the IGamePluginControlService control-plane contract.");
        var controls = ResolveControls(worldType, cancellationToken);
        var ns = type.ContainingNamespace.IsGlobalNamespace ? string.Empty : type.ContainingNamespace.ToDisplayString();
        var controlNs = controlServiceType.ContainingNamespace.ToDisplayString();
        var eventCallback = PluginServerEventCallbackResolver.Resolve(compilation, worldType, cancellationToken);
        return new PluginServerFacadeModel(
            ns,
            AccessibilityText(type.DeclaredAccessibility),
            type.Name,
            ServerInterfaceName(worldType),
            SetupInterfaceName(type.Name),
            TypeName(worldType),
            PluginServerWorldExtensionSuffixResolver.Resolve(compilation, worldType, cancellationToken),
            PluginServerXmlDocumentation.FromSymbol(
                worldType,
                "Generated plugin-side facade for the remote world domain.",
                cancellationToken),
            TypeName(controlServiceType),
            "global::" + controlNs + ".LiveSettingUpdate",
            new EquatableArray<PluginServerForwardedMethod>(
                ResolveMethods(worldType, new Dictionary<string, ServiceWrapperBuilder>(StringComparer.Ordinal), cancellationToken)),
            new EquatableArray<PluginServerControlProperty>(controls),
            eventCallback is null ? null : TypeName(eventCallback.Value.Type),
            eventCallback?.ProvideSuffix);
    }

    private static INamedTypeSymbol? ResolveWorldType(INamedTypeSymbol type)
    {
        foreach (var candidate in type.Interfaces)
        {
            if (HasAttribute(candidate, DotBoxDGenerationNames.Metadata.DotBoxDServiceAttribute))
            {
                return candidate;
            }
        }

        return null;
    }

    private static INamedTypeSymbol? ResolveControlService(
        Compilation compilation,
        INamedTypeSymbol worldType)
    {
        var worldNamespace = worldType.ContainingNamespace.ToDisplayString();
        return compilation.GetTypeByMetadataName(worldNamespace + ".Ipc.IGamePluginControlService");
    }

    private static PluginServerControlProperty[] ResolveControls(
        INamedTypeSymbol worldType,
        CancellationToken cancellationToken)
    {
        var controls = new List<PluginServerControlProperty>();
        var seenProperties = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in MembersIncludingInherited(worldType))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is not IPropertySymbol
                {
                    IsStatic: false,
                    GetMethod: not null,
                    SetMethod: null,
                    Type: INamedTypeSymbol propertyType
                } property ||
                !HasAttribute(propertyType, DotBoxDGenerationNames.Metadata.DotBoxDServiceAttribute) ||
                !seenProperties.Add(property.Name))
            {
                continue;
            }

            controls.Add(new PluginServerControlProperty(
                property.Name,
                TypeName(propertyType),
                PluginServerXmlDocumentation.FromSymbol(
                    property,
                    "Accesses the server's " + property.Name + " domain control after StartAsync.",
                    cancellationToken),
                property.Name + "PluginControl",
                propertyType.Name + "Accumulator",
                new EquatableArray<PluginServerForwardedMethod>(
                    ResolveMethods(propertyType, new Dictionary<string, ServiceWrapperBuilder>(StringComparer.Ordinal), cancellationToken)),
                new EquatableArray<PluginServerServiceWrapper>(ResolveServiceWrappers(propertyType, cancellationToken))));
        }

        return controls.ToArray();
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
            if (member is IMethodSymbol
                {
                    MethodKind: MethodKind.Ordinary,
                    IsStatic: false,
                    IsGenericMethod: false
                } method &&
                !IsControlPlaneMember(method.ContainingType))
            {
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

    private static PluginServerServiceWrapper[] ResolveServiceWrappers(
        INamedTypeSymbol controlType,
        CancellationToken cancellationToken)
    {
        var serviceWrappers = new Dictionary<string, ServiceWrapperBuilder>(StringComparer.Ordinal);
        ResolveMethods(controlType, serviceWrappers, cancellationToken);
        return serviceWrappers.Values
            .Select(static wrapper => new PluginServerServiceWrapper(
                wrapper.Type,
                wrapper.WrapperName,
                wrapper.Documentation,
                new EquatableArray<PluginServerForwardedProperty>(wrapper.Properties.ToArray()),
                new EquatableArray<PluginServerForwardedMethod>(wrapper.Methods.ToArray())))
            .ToArray();
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
            ServiceWrapperName(serviceType),
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
            !HasAttribute(namedServiceType, DotBoxDGenerationNames.Metadata.DotBoxDServiceAttribute))
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
        var seenProperties = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in MembersIncludingInherited(serviceType))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is IPropertySymbol
                {
                    IsStatic: false,
                    GetMethod: not null,
                    SetMethod: null
                } property &&
                seenProperties.Add(property.Name))
            {
                wrapper.Properties.Add(new PluginServerForwardedProperty(
                    property.Name,
                    TypeName(property.Type),
                    PluginServerXmlDocumentation.FromSymbol(
                        property,
                        "Forwards the " + property.Name + " property from the remote domain service.",
                        cancellationToken)));
            }
        }

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
