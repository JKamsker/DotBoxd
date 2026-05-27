using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ShaRPC.SourceGenerator;

internal readonly record struct ServiceResult(
    ServiceModel? Model,
    GeneratorError? Error,
    EquatableArray<MethodDiagnostic> MethodDiagnostics,
    ServiceDiagnostic? ServiceDiagnostic);

internal readonly record struct GeneratorError(string Where, string Message);

internal readonly record struct DiagnosticLocation(
    string FilePath,
    int Start,
    int Length,
    int StartLine,
    int StartCharacter,
    int EndLine,
    int EndCharacter)
{
    public static DiagnosticLocation FromLocation(Location? location)
    {
        if (location is null || !location.IsInSource)
        {
            return default;
        }

        var lineSpan = location.GetLineSpan();
        return new DiagnosticLocation(
            lineSpan.Path,
            location.SourceSpan.Start,
            location.SourceSpan.Length,
            lineSpan.StartLinePosition.Line,
            lineSpan.StartLinePosition.Character,
            lineSpan.EndLinePosition.Line,
            lineSpan.EndLinePosition.Character);
    }

    public Location ToLocation()
    {
        if (Length <= 0)
        {
            return Location.None;
        }

        return Location.Create(
            FilePath,
            new TextSpan(Start, Length),
            new LinePositionSpan(
                new LinePosition(StartLine, StartCharacter),
                new LinePosition(EndLine, EndCharacter)));
    }
}

/// <summary>Diagnostic about one method (SHARPC002) — emitted while still producing the rest of the service.</summary>
internal readonly record struct MethodDiagnostic(
    string InterfaceName,
    string MethodName,
    string Reason,
    DiagnosticLocation Location = default);

/// <summary>Diagnostic about the service as a whole (SHARPC003) — service is skipped entirely.</summary>
internal readonly record struct ServiceDiagnostic(
    string InterfaceName,
    string Reason,
    DiagnosticLocation Location = default);
