using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ShaRPC.SourceGenerator;

internal readonly record struct ServiceResult(
    ServiceModel? Model,
    GeneratorError? Error,
    EquatableArray<MethodDiagnostic> MethodDiagnostics,
    EquatableArray<DiagnosticLocation> MethodLocations,
    DiagnosticLocation ServiceLocation,
    string QualifiedInterfaceName,
    ServiceDiagnostic? ServiceDiagnostic,
    ExistingTypeCollisionDiagnostic? ExistingTypeCollision = null);

internal readonly record struct GeneratorError(string Where, string Message);

internal readonly record struct UnsupportedMemberDiagnostic(
    string Reason,
    DiagnosticLocation Location);

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

internal readonly record struct ExistingTypeCollisionDiagnostic(
    string InterfaceName,
    string Reason,
    ExistingTypeKey ExistingType);

internal readonly record struct ServiceIdentity(
    string Namespace,
    string InterfaceName,
    string ServiceName,
    string QualifiedInterfaceName)
{
    public static ServiceIdentity From(ServiceResult result) =>
        new(
            result.Model!.Namespace,
            result.Model.InterfaceName,
            result.Model.ServiceName,
            result.QualifiedInterfaceName);

    public static ServiceIdentity From(ServiceModel model) =>
        new(
            model.Namespace,
            model.InterfaceName,
            model.ServiceName,
            IdentifierHelpers.QualifyTypeName(model.Namespace, model.InterfaceName));
}

internal readonly record struct GeneratedServiceIdentity(
    string Namespace,
    string InterfaceName)
{
    public static GeneratedServiceIdentity From(ServiceResult result) =>
        new(result.Model!.Namespace, result.Model.InterfaceName);
}

internal readonly record struct RejectedServiceIdentity(string QualifiedInterfaceName)
{
    public static RejectedServiceIdentity? From(ServiceResult result)
    {
        if (result.Model is null &&
            (result.ServiceDiagnostic is not null || result.ExistingTypeCollision is not null) &&
            !string.IsNullOrEmpty(result.QualifiedInterfaceName))
        {
            return new RejectedServiceIdentity(result.QualifiedInterfaceName);
        }

        return null;
    }
}
