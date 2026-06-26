using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal readonly record struct GeneratorGuardedValue<T>(
    T? Value,
    bool HasValue,
    GeneratorFailureDiagnostic? Diagnostic)
{
    public static GeneratorGuardedValue<T> Success(T value)
        => new(value, HasValue: true, Diagnostic: null);

    public static GeneratorGuardedValue<T> Failure(GeneratorFailureDiagnostic diagnostic)
        => new(default, HasValue: false, diagnostic);
}

internal sealed record GeneratorFailureDiagnostic(
    string Stage,
    string ExceptionType,
    string Message,
    PluginDiagnosticLocation? Location)
{
    public Diagnostic ToDiagnostic()
        => Diagnostic.Create(
            PluginAnalyzerDiagnostics.SourceGeneratorFailureRule,
            Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None,
            Stage,
            ExceptionType,
            Message);
}
