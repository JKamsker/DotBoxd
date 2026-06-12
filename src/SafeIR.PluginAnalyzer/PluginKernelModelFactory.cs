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
                "GamePlugin id must be a non-empty string.");
            return new PluginKernelModelResult(null, diagnostic);
        }

        if (eventTypes.Count == 0)
        {
            var diagnostic = PluginKernelDiagnostic.Create(
                declaration.Identifier,
                "Game plugins must implement IEventKernel<TEvent>.");
            return new PluginKernelModelResult(null, diagnostic);
        }

        if (eventTypes.Count > 1)
        {
            var diagnostic = PluginKernelDiagnostic.Create(
                declaration.Identifier,
                "Game plugins must implement exactly one IEventKernel<TEvent>.");
            return new PluginKernelModelResult(null, diagnostic);
        }

        var validatedPluginId = pluginId!;
        var eventType = eventTypes[0];
        try
        {
            var shouldHandle = InterfaceMethodSyntax(context, type, SafeIrGenerationNames.Entrypoints.ShouldHandle, cancellationToken);
            var handle = InterfaceMethodSyntax(context, type, SafeIrGenerationNames.Entrypoints.Handle, cancellationToken);
            var eventProperties = new EquatableArray<EventPropertyModel>(PluginSymbolReader.EventProperties(eventType));
            if (eventProperties.Any(p => p.Type == SafeIrGenerationNames.ManifestTypes.Unsupported))
            {
                throw new NotSupportedException("Kernel event properties must use supported scalar types.");
            }

            var liveSettings = new EquatableArray<LiveSettingModel>(
                PluginSymbolReader.LiveSettings(type, context.SemanticModel, cancellationToken));
            if (liveSettings.Any(s => s.Type == SafeIrGenerationNames.ManifestTypes.Unsupported))
            {
                throw new NotSupportedException("Live settings must use supported scalar types.");
            }

            ValidateGeneratedParameterNames(eventProperties, liveSettings);

            var eventParameterName = shouldHandle.ParameterList.Parameters
                .ElementAtOrDefault(SafeIrGenerationNames.KernelMethodParameters.EventIndex)
                ?.Identifier.ValueText ??
                SafeIrGenerationNames.DefaultEventParameterName;
            var contextParameterName = shouldHandle.ParameterList.Parameters
                .ElementAtOrDefault(SafeIrGenerationNames.KernelMethodParameters.ContextIndex)
                ?.Identifier.ValueText ??
                SafeIrGenerationNames.DefaultContextParameterName;
            var handleEventParameterName = handle.ParameterList.Parameters
                .ElementAtOrDefault(SafeIrGenerationNames.KernelMethodParameters.EventIndex)
                ?.Identifier.ValueText ??
                SafeIrGenerationNames.DefaultEventParameterName;
            var handleContextParameterName = handle.ParameterList.Parameters
                .ElementAtOrDefault(SafeIrGenerationNames.KernelMethodParameters.ContextIndex)
                ?.Identifier.ValueText ??
                SafeIrGenerationNames.DefaultContextParameterName;
            var shouldHandleContext = new SafeIrExpressionLoweringContext(
                eventParameterName,
                eventProperties,
                liveSettings,
                context.SemanticModel,
                cancellationToken);
            var shouldHandleBody = SafeIrConditionBodyModelFactory.Create(
                ReturnExpression(shouldHandle),
                shouldHandleContext);

            var handleModel = SafeIrHandleModelFactory.Create(
                handle,
                handleEventParameterName,
                handleContextParameterName,
                eventProperties,
                liveSettings,
                context.SemanticModel,
                cancellationToken);
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
                ManifestEffects: SafeIrManifestEffectModel.Create(shouldHandleBody, handleModel));
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
        var interfaceMember = type.AllInterfaces
            .Where(i => string.Equals(
                i.OriginalDefinition.ToDisplayString(),
                SafeIrGenerationNames.Metadata.EventKernelInterface,
                StringComparison.Ordinal))
            .SelectMany(i => i.GetMembers(methodName))
            .OfType<IMethodSymbol>()
            .FirstOrDefault();
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

    private static string PackageName(string kernelName)
        => kernelName.EndsWith(SafeIrGenerationNames.KernelSuffix, StringComparison.Ordinal)
            ? kernelName.Substring(0, kernelName.Length - SafeIrGenerationNames.KernelSuffix.Length) +
                SafeIrGenerationNames.PluginPackageSuffix
            : kernelName + SafeIrGenerationNames.PluginPackageSuffix;

    private static ExpressionSyntax ReturnExpression(MethodDeclarationSyntax method)
    {
        if (method.ExpressionBody?.Expression is { } expression)
        {
            return expression;
        }

        if (method.Body is null ||
            method.Body.Statements.Count != 1 ||
            method.Body.Statements[0] is not ReturnStatementSyntax ret ||
            ret.Expression is null)
        {
            throw new NotSupportedException("Kernel ShouldHandle must return exactly one expression.");
        }

        return ret.Expression;
    }
}
