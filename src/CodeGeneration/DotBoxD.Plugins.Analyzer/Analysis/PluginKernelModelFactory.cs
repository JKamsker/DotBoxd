using DotBoxD.Plugins.Analyzer.Analysis.HookChains;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class PluginKernelModelFactory
{
    public static PluginKernelModelResult? Create(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TargetSymbol is not INamedTypeSymbol type ||
            context.TargetNode is not ClassDeclarationSyntax declaration)
        {
            return null;
        }

        var pluginId = PluginSymbolReader.PluginId(context.Attributes) ?? KernelId(type.Name);
        var eventTypes = PluginSymbolReader.EventTypes(type);
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return Fail(declaration, "Plugin id must be a non-empty string.");
        }

        if (type.IsGenericType || type.TypeParameters.Length > 0)
        {
            return Fail(declaration, $"Plugin kernel '{type.Name}' cannot be generic.");
        }

        if (type.ContainingType is not null)
        {
            return Fail(
                declaration,
                $"Plugin kernels must be top-level types; '{type.ToDisplayString()}' is nested.");
        }

        if (eventTypes.Count == 0)
        {
            return Fail(declaration, "Plugin kernels must implement IEventKernel<TEvent>.");
        }

        if (eventTypes.Count > 1)
        {
            return Fail(declaration, "Plugin kernels must implement exactly one IEventKernel<TEvent>.");
        }

        var validatedPluginId = pluginId!;
        var eventType = eventTypes[0];
        try
        {
            var shouldHandle = InterfaceMethodSyntax(context, type, DotBoxDGenerationNames.Entrypoints.ShouldHandle, cancellationToken);
            var handle = InterfaceMethodSyntax(context, type, DotBoxDGenerationNames.Entrypoints.Handle, cancellationToken);
            var eventProperties = PluginSymbolReader.EventProperties(eventType);
            if (ContainsUnsupported(eventProperties))
            {
                throw new NotSupportedException(PluginKernelUnsupportedShapeMessage.EventProperties(eventType));
            }

            var liveSettings = PluginSymbolReader.LiveSettings(type, context.SemanticModel, cancellationToken);
            if (ContainsUnsupported(liveSettings))
            {
                throw new NotSupportedException("Live settings must use supported scalar types.");
            }

            ValidateGeneratedParameterNames(eventProperties, liveSettings);

            var eventParameterName = ParameterName(
                shouldHandle,
                DotBoxDGenerationNames.KernelMethodParameters.EventIndex,
                DotBoxDGenerationNames.DefaultEventParameterName);
            var contextParameterName = ParameterName(
                shouldHandle,
                DotBoxDGenerationNames.KernelMethodParameters.ContextIndex,
                DotBoxDGenerationNames.DefaultContextParameterName);
            var handleEventParameterName = ParameterName(
                handle,
                DotBoxDGenerationNames.KernelMethodParameters.EventIndex,
                DotBoxDGenerationNames.DefaultEventParameterName);
            var handleContextParameterName = ParameterName(
                handle,
                DotBoxDGenerationNames.KernelMethodParameters.ContextIndex,
                DotBoxDGenerationNames.DefaultContextParameterName);
            // Collectors for the whole kernel: ShouldHandle + Handle lowering deposit every capability the
            // verified IR needs (Send, [HostBinding] calls, gated event-property reads) and every extra
            // sandbox effect a [HostBinding] declares. Sorted for deterministic, incrementality-stable output.
            var capabilities = new SortedSet<string>(StringComparer.Ordinal);
            var effects = new SortedSet<string>(StringComparer.Ordinal);
            var shouldHandleContext = new DotBoxDExpressionLoweringContext(
                eventParameterName,
                eventProperties,
                liveSettings,
                context.SemanticModel,
                cancellationToken,
                capabilities: capabilities,
                effects: effects);
            var shouldHandleBody = DotBoxDShouldHandleBodyModelFactory.Create(shouldHandle, shouldHandleContext);

            // Issue #51: mine host-readable index metadata from the kernel's ShouldHandle the same way inline
            // chains mine it from .Where(...). Live-setting and other non-constant comparisons resolve to no
            // constant, so they stay non-indexed (default, false) exactly as before.
            var (indexPredicates, indexCoversPredicate) = HookChainIndexPredicateExtractor.ExtractFromShouldHandle(
                shouldHandle,
                eventParameterName,
                eventProperties,
                context.SemanticModel,
                cancellationToken);

            var handleModel = DotBoxDHandleModelFactory.Create(
                handle,
                handleEventParameterName,
                handleContextParameterName,
                eventProperties,
                liveSettings,
                context.SemanticModel,
                cancellationToken,
                capabilities,
                effects);
            var handleBody = DotBoxDHandleBodyModelFactory.FromSend(handleModel);
            var model = new PluginKernelModel(
                PluginId: validatedPluginId,
                Namespace: type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString(),
                KernelName: type.Name,
                PackageName: PackageName(type.Name),
                EventName: EventTypeName.HookOrQualified(eventType),
                EventParameterName: eventParameterName,
                ContextParameterName: contextParameterName,
                HandleEventParameterName: handleEventParameterName,
                HandleContextParameterName: handleContextParameterName,
                EventProperties: eventProperties,
                LiveSettings: liveSettings,
                ShouldHandle: shouldHandleBody,
                HandleBody: handleBody,
                HandleReturnTypeSource: DotBoxDGenerationNames.TypeNames.GlobalSandboxType + ".Unit",
                ManifestEffects: DotBoxDManifestEffectModel.Create(shouldHandleBody, handleBody, effects),
                RequiredCapabilities: EquatableArray<string>.FromOwned([.. capabilities]),
                IndexPredicates: indexPredicates,
                IndexCoversPredicate: indexCoversPredicate);
            return new PluginKernelModelResult(model, null);
        }
        catch (NotSupportedException ex)
        {
            return Fail(declaration, ex.Message);
        }
    }

    private static PluginKernelModelResult Fail(ClassDeclarationSyntax declaration, string message)
        => new(null, PluginKernelDiagnostic.Create(declaration.Identifier, message));

    private static void ValidateGeneratedParameterNames(
        EquatableArray<EventPropertyModel> eventProperties,
        EquatableArray<LiveSettingModel> liveSettings)
    {
        var eventParameterNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in eventProperties)
        {
            var parameterName = DotBoxDExpressionModelFactory.EventVariable(property.Name);
            if (!eventParameterNames.Add(parameterName))
            {
                throw new NotSupportedException(
                    $"Event property '{property.Name}' generates duplicate parameter '{parameterName}'.");
            }
        }

        foreach (var setting in liveSettings)
        {
            if (eventParameterNames.Contains(setting.Name))
            {
                throw new NotSupportedException(
                    $"Live setting '{setting.Name}' conflicts with a generated event parameter.");
            }
        }
    }

    private static MethodDeclarationSyntax InterfaceMethodSyntax(
        GeneratorAttributeSyntaxContext context,
        INamedTypeSymbol type,
        string methodName,
        CancellationToken cancellationToken)
    {
        var interfaceMember = InterfaceMethod(type, methodName);
        if (interfaceMember is null)
        {
            throw new NotSupportedException($"Kernel must implement IEventKernel.{methodName}.");
        }

        var implementation = type.FindImplementationForInterfaceMember(interfaceMember) as IMethodSymbol;
        if (implementation is null)
        {
            throw new NotSupportedException($"Kernel {methodName} implementation could not be resolved.");
        }

        foreach (var reference in implementation.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is MethodDeclarationSyntax method)
            {
                return method;
            }
        }

        throw new NotSupportedException($"Kernel {methodName} must be declared in source.");
    }

    private static string ParameterName(MethodDeclarationSyntax method, int index, string fallback)
        => method.ParameterList.Parameters.Count > index
            ? method.ParameterList.Parameters[index].Identifier.ValueText
            : fallback;

    private static IMethodSymbol? InterfaceMethod(INamedTypeSymbol type, string methodName)
    {
        foreach (var @interface in type.AllInterfaces)
        {
            if (!string.Equals(
                    @interface.OriginalDefinition.ToDisplayString(),
                    DotBoxDMetadataNames.EventKernelInterface,
                    StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var member in @interface.GetMembers(methodName))
            {
                if (member is IMethodSymbol method)
                {
                    return method;
                }
            }
        }

        return null;
    }

    private static string PackageName(string kernelName)
        => kernelName.EndsWith(DotBoxDGenerationNames.KernelSuffix, StringComparison.Ordinal)
            ? kernelName.Substring(0, kernelName.Length - DotBoxDGenerationNames.KernelSuffix.Length) +
                DotBoxDGenerationNames.PluginPackageSuffix
            : kernelName + DotBoxDGenerationNames.PluginPackageSuffix;

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

    private static bool ContainsUnsupported(EquatableArray<EventPropertyModel> eventProperties)
    {
        for (var i = 0; i < eventProperties.Count; i++)
        {
            if (eventProperties[i].Type == DotBoxDGenerationNames.ManifestTypes.Unsupported)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsUnsupported(EquatableArray<LiveSettingModel> liveSettings)
    {
        for (var i = 0; i < liveSettings.Count; i++)
        {
            if (liveSettings[i].Type == DotBoxDGenerationNames.ManifestTypes.Unsupported)
            {
                return true;
            }
        }

        return false;
    }
}
