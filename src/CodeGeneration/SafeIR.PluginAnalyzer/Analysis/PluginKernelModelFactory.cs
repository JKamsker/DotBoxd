namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class PluginKernelModelFactory
{
    public static PluginKernelModelResult? Create(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TargetSymbol is not INamedTypeSymbol type ||
            context.TargetNode is not ClassDeclarationSyntax declaration) {
            return null;
        }

        var pluginId = PluginSymbolReader.PluginId(context.Attributes);
        var eventTypes = PluginSymbolReader.EventTypes(type);
        if (string.IsNullOrWhiteSpace(pluginId)) {
            var diagnostic = PluginKernelDiagnostic.Create(
                declaration.Identifier,
                "Plugin id must be a non-empty string.");
            return new PluginKernelModelResult(null, diagnostic);
        }

        if (eventTypes.Count == 0)
        {
            var diagnostic = PluginKernelDiagnostic.Create(
                declaration.Identifier,
                "Plugin kernels must implement IEventKernel<TEvent>.");
            return new PluginKernelModelResult(null, diagnostic);
        }

        if (eventTypes.Count > 1)
        {
            var diagnostic = PluginKernelDiagnostic.Create(
                declaration.Identifier,
                "Plugin kernels must implement exactly one IEventKernel<TEvent>.");
            return new PluginKernelModelResult(null, diagnostic);
        }

        var validatedPluginId = pluginId!;
        var eventType = eventTypes[0];
        try
        {
            var shouldHandle = InterfaceMethodSyntax(context, type, SafeIrGenerationNames.Entrypoints.ShouldHandle, cancellationToken);
            var handle = InterfaceMethodSyntax(context, type, SafeIrGenerationNames.Entrypoints.Handle, cancellationToken);
            var eventProperties = PluginSymbolReader.EventProperties(eventType);
            if (ContainsUnsupported(eventProperties))
            {
                throw new NotSupportedException("Kernel event properties must use supported scalar types.");
            }

            var liveSettings = PluginSymbolReader.LiveSettings(type, context.SemanticModel, cancellationToken);
            if (ContainsUnsupported(liveSettings))
            {
                throw new NotSupportedException("Live settings must use supported scalar types.");
            }

            ValidateGeneratedParameterNames(eventProperties, liveSettings);

            var eventParameterName = ParameterName(
                shouldHandle,
                SafeIrGenerationNames.KernelMethodParameters.EventIndex,
                SafeIrGenerationNames.DefaultEventParameterName);
            var contextParameterName = ParameterName(
                shouldHandle,
                SafeIrGenerationNames.KernelMethodParameters.ContextIndex,
                SafeIrGenerationNames.DefaultContextParameterName);
            var handleEventParameterName = ParameterName(
                handle,
                SafeIrGenerationNames.KernelMethodParameters.EventIndex,
                SafeIrGenerationNames.DefaultEventParameterName);
            var handleContextParameterName = ParameterName(
                handle,
                SafeIrGenerationNames.KernelMethodParameters.ContextIndex,
                SafeIrGenerationNames.DefaultContextParameterName);
            // Collectors for the whole kernel: ShouldHandle + Handle lowering deposit every capability the
            // verified IR needs (Send, [HostBinding] calls, gated event-property reads) and every extra
            // sandbox effect a [HostBinding] declares. Sorted for deterministic, incrementality-stable output.
            var capabilities = new SortedSet<string>(StringComparer.Ordinal);
            var effects = new SortedSet<string>(StringComparer.Ordinal);
            var shouldHandleContext = new SafeIrExpressionLoweringContext(
                eventParameterName,
                eventProperties,
                liveSettings,
                context.SemanticModel,
                cancellationToken,
                capabilities: capabilities,
                effects: effects);
            var shouldHandleBody = SafeIrShouldHandleBodyModelFactory.Create(shouldHandle, shouldHandleContext);

            var handleModel = SafeIrHandleModelFactory.Create(
                handle,
                handleEventParameterName,
                handleContextParameterName,
                eventProperties,
                liveSettings,
                context.SemanticModel,
                cancellationToken,
                capabilities,
                effects);
            var model = new PluginKernelModel(
                PluginId: validatedPluginId,
                Namespace: type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString(),
                KernelName: type.Name,
                PackageName: PackageName(type.Name),
                EventName: eventType.MetadataName,
                EventParameterName: eventParameterName,
                ContextParameterName: contextParameterName,
                HandleEventParameterName: handleEventParameterName,
                HandleContextParameterName: handleContextParameterName,
                EventProperties: eventProperties,
                LiveSettings: liveSettings,
                ShouldHandle: shouldHandleBody,
                Handle: handleModel,
                ManifestEffects: SafeIrManifestEffectModel.Create(shouldHandleBody, handleModel, effects),
                RequiredCapabilities: EquatableArray<string>.FromOwned([.. capabilities]));
            return new PluginKernelModelResult(model, null);
        }
        catch (NotSupportedException ex)
        {
            var diagnostic = PluginKernelDiagnostic.Create(declaration.Identifier, ex.Message);
            return new PluginKernelModelResult(null, diagnostic);
        }
    }

    private static void ValidateGeneratedParameterNames(
        EquatableArray<EventPropertyModel> eventProperties,
        EquatableArray<LiveSettingModel> liveSettings)
    {
        var eventParameterNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in eventProperties)
        {
            var parameterName = SafeIrExpressionModelFactory.EventVariable(property.Name);
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
                    SafeIrGenerationNames.Metadata.EventKernelInterface,
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
        => kernelName.EndsWith(SafeIrGenerationNames.KernelSuffix, StringComparison.Ordinal)
            ? kernelName.Substring(0, kernelName.Length - SafeIrGenerationNames.KernelSuffix.Length) +
                SafeIrGenerationNames.PluginPackageSuffix
            : kernelName + SafeIrGenerationNames.PluginPackageSuffix;

    private static bool ContainsUnsupported(EquatableArray<EventPropertyModel> eventProperties)
    {
        for (var i = 0; i < eventProperties.Count; i++) {
            if (eventProperties[i].Type == SafeIrGenerationNames.ManifestTypes.Unsupported) {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsUnsupported(EquatableArray<LiveSettingModel> liveSettings)
    {
        for (var i = 0; i < liveSettings.Count; i++) {
            if (liveSettings[i].Type == SafeIrGenerationNames.ManifestTypes.Unsupported) {
                return true;
            }
        }

        return false;
    }
}
