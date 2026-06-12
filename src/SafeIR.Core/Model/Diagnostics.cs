namespace SafeIR;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record SandboxDiagnostic(
    string Code,
    string Message,
    DiagnosticSeverity Severity = DiagnosticSeverity.Error,
    SourceSpan? Span = null);

public sealed record SourceSpan(int Line, int Column);

public sealed class SandboxValidationException(
    IReadOnlyList<SandboxDiagnostic> diagnostics)
    : Exception(string.Join(Environment.NewLine, diagnostics.Select(d => $"{d.Code}: {d.Message}")))
{
    public IReadOnlyList<SandboxDiagnostic> Diagnostics { get; } = diagnostics;
}

public sealed class SandboxRuntimeException : Exception
{
    public SandboxRuntimeException(SandboxError error)
        : base(error.SafeMessage)
    {
        Error = error;
    }

    public SandboxError Error { get; }
}
