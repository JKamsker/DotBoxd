namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

internal sealed record PluginKernelDiagnostic(string Message, PluginDiagnosticLocation? Location)
{
    public static PluginKernelDiagnostic Create(SyntaxToken token, string message)
        => new(message, PluginDiagnosticLocation.From(token.GetLocation()));

    public Diagnostic ToDiagnostic()
        => Diagnostic.Create(
            PluginAnalyzerDiagnostics.UnsupportedKernelShapeRule,
            Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None,
            Message);
}

internal readonly record struct PluginDiagnosticLocation(
    string FilePath,
    int Start,
    int Length,
    int StartLine,
    int StartCharacter,
    int EndLine,
    int EndCharacter)
{
    public static PluginDiagnosticLocation From(Location location)
    {
        var lineSpan = location.GetLineSpan();
        var span = location.SourceSpan;
        return new PluginDiagnosticLocation(
            lineSpan.Path,
            span.Start,
            span.Length,
            lineSpan.StartLinePosition.Line,
            lineSpan.StartLinePosition.Character,
            lineSpan.EndLinePosition.Line,
            lineSpan.EndLinePosition.Character);
    }

    public Location ToLocation()
        => Location.Create(
            FilePath,
            new TextSpan(Start, Length),
            new LinePositionSpan(
                new LinePosition(StartLine, StartCharacter),
                new LinePosition(EndLine, EndCharacter)));
}
