namespace SafeIR.Validation;

using SafeIR;

public sealed class ModuleValidator
{
    public ModuleValidationResult Validate(SandboxModule module, IBindingCatalog bindings, SandboxPolicy? policy = null)
    {
        var diagnostics = new List<SandboxDiagnostic>();
        StructuralValidator.Validate(module, diagnostics);
        if (diagnostics.Count > 0) {
            return ModuleValidationResult.Failure(diagnostics);
        }

        var analyzer = new FunctionAnalyzer(module, bindings, diagnostics);
        var functions = analyzer.AnalyzeAll();
        PolicyResolver.Validate(module, bindings, policy, functions, analyzer.RequiredCapabilities, diagnostics);

        return new ModuleValidationResult(
            diagnostics.All(d => d.Severity != DiagnosticSeverity.Error),
            diagnostics,
            functions,
            functions.Values.Aggregate(SandboxEffect.None, (current, next) => current | next.Effects),
            analyzer.RequiredCapabilities);
    }
}
