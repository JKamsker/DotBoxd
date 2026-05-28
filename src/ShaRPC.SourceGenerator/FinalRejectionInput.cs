using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace ShaRPC.SourceGenerator;

internal readonly record struct FinalRejectionInput(
    ServiceModel? Model,
    string QualifiedInterfaceName,
    ServiceDiagnostic? ServiceDiagnostic,
    ExistingTypeCollisionDiagnostic? ExistingTypeCollision)
{
    public static FinalRejectionInput From(ServiceResult result) =>
        new(
            result.Model,
            result.QualifiedInterfaceName,
            result.ServiceDiagnostic,
            result.ExistingTypeCollision);

    public static ServiceResult ToServiceResult(FinalRejectionInput input) =>
        new(
            input.Model,
            Error: null,
            MethodDiagnostics: EquatableArray<MethodDiagnostic>.Empty,
            MethodLocations: EquatableArray<DiagnosticLocation>.Empty,
            ServiceLocation: default,
            input.QualifiedInterfaceName,
            input.ServiceDiagnostic,
            input.ExistingTypeCollision);
}

internal static class FinalRejectionInputs
{
    public static ImmutableArray<ServiceResult> ToServiceResults(
        ImmutableArray<FinalRejectionInput> inputs,
        CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<ServiceResult>(inputs.Length);
        foreach (var input in inputs)
        {
            ct.ThrowIfCancellationRequested();
            builder.Add(FinalRejectionInput.ToServiceResult(input));
        }

        return builder.ToImmutable();
    }
}
