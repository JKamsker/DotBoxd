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
        var eventType = PluginSymbolReader.EventType(type);
        if (string.IsNullOrWhiteSpace(pluginId)) {
            var diagnostic = Diagnostic.Create(
                PluginAnalyzerDiagnostics.UnsupportedKernelShapeRule,
                declaration.Identifier.GetLocation(),
                "GamePlugin id must be a non-empty string.");
            return new PluginKernelModelResult(null, diagnostic);
        }

        var validatedPluginId = pluginId!;
        if (eventType is null)
        {
            var diagnostic = Diagnostic.Create(
                PluginAnalyzerDiagnostics.UnsupportedKernelShapeRule,
                declaration.Identifier.GetLocation(),
                "Game plugins must implement IEventKernel<TEvent>.");
            return new PluginKernelModelResult(null, diagnostic);
        }

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
            var shouldHandleExpression = SafeIrExpressionModelFactory.Create(
                ReturnExpression(shouldHandle),
                eventParameterName,
                eventProperties,
                liveSettings);
            if (!string.Equals(shouldHandleExpression.Type, SafeIrGenerationNames.ManifestTypes.Bool, StringComparison.Ordinal))
            {
                throw new NotSupportedException("Kernel ShouldHandle must lower to a bool expression.");
            }

            var handleModel = SafeIrHandleModelFactory.Create(
                handle,
                handleEventParameterName,
                handleContextParameterName,
                eventProperties,
                liveSettings);
            var model = new PluginKernelModel(
                PluginId: validatedPluginId,
                Namespace: type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString(),
                KernelName: type.Name,
                PackageName: PackageName(type.Name),
                EventName: eventType.Name,
                EventParameterName: eventParameterName,
                ContextParameterName: contextParameterName,
                HandleEventParameterName: handleEventParameterName,
                HandleContextParameterName: handleContextParameterName,
                EventProperties: eventProperties,
                LiveSettings: liveSettings,
                ShouldHandle: shouldHandleExpression,
                Handle: handleModel,
                ManifestEffects: SafeIrManifestEffectModel.Create(shouldHandleExpression, handleModel));
            return new PluginKernelModelResult(model, null);
        }
        catch (NotSupportedException ex)
        {
            var diagnostic = Diagnostic.Create(
                PluginAnalyzerDiagnostics.UnsupportedKernelShapeRule,
                declaration.Identifier.GetLocation(),
                ex.Message);
            return new PluginKernelModelResult(null, diagnostic);
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
