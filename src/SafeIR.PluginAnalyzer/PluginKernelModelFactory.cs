namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class PluginKernelModelFactory
{
    public static GeneratedPluginPackageResult? Create(
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
        if (pluginId is null || eventType is null) {
            return null;
        }

        var shouldHandle = declaration.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => string.Equals(m.Identifier.ValueText, "ShouldHandle", StringComparison.Ordinal));
        var handle = declaration.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => string.Equals(m.Identifier.ValueText, "Handle", StringComparison.Ordinal));
        if (shouldHandle is null || handle is null) {
            return null;
        }

        try
        {
            var model = new PluginKernelModel(
                PluginId: pluginId,
                Namespace: type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString(),
                KernelName: type.Name,
                PackageName: PackageName(type.Name),
                EventName: eventType.Name,
                EventParameterName: shouldHandle.ParameterList.Parameters.FirstOrDefault()?.Identifier.ValueText ?? "e",
                ContextParameterName: shouldHandle.ParameterList.Parameters.Skip(1).FirstOrDefault()?.Identifier.ValueText ?? "ctx",
                HandleEventParameterName: handle.ParameterList.Parameters.FirstOrDefault()?.Identifier.ValueText ?? "e",
                HandleContextParameterName: handle.ParameterList.Parameters.Skip(1).FirstOrDefault()?.Identifier.ValueText ?? "ctx",
                EventProperties: PluginSymbolReader.EventProperties(eventType),
                LiveSettings: PluginSymbolReader.LiveSettings(type),
                ShouldHandle: shouldHandle,
                Handle: handle);
            return new GeneratedPluginPackageResult(SafeIrPackageSourceEmitter.Emit(model), null);
        }
        catch (NotSupportedException ex)
        {
            var diagnostic = Diagnostic.Create(
                PluginAnalyzerDiagnostics.UnsupportedKernelShapeRule,
                declaration.Identifier.GetLocation(),
                ex.Message);
            return new GeneratedPluginPackageResult(null, diagnostic);
        }
    }

    private static string PackageName(string kernelName)
        => kernelName.EndsWith("Kernel", StringComparison.Ordinal)
            ? kernelName.Substring(0, kernelName.Length - "Kernel".Length) + "PluginPackage"
            : kernelName + "PluginPackage";
}
