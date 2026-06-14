using Microsoft.CodeAnalysis;

namespace DotBoxd.Services.SourceGenerator;

internal static class ServiceResultValidationPipeline
{
    public static IncrementalValuesProvider<ServiceResult> Apply(
        IncrementalValuesProvider<ServiceResult> results,
        IncrementalValueProvider<ExistingTypeIndex> existingTypes)
    {
        results = results
            .Combine(existingTypes)
            .Select(static (pair, ct) =>
                GeneratedTypeCollisionValidator.ApplyPrimaryTypes(pair.Left, pair.Right, ct))
            .WithTrackingName("ExistingTypeValidatedServiceResults");

        var generatedServiceIdentities = results
            .Where(static r => r.Model is not null)
            .Select(static (r, _) => GeneratedServiceIdentity.From(r))
            .WithTrackingName("GeneratedServiceNameInputs");

        var generatedServiceNames = generatedServiceIdentities
            .Collect()
            .Select(static (arr, ct) => GeneratedServiceNameIndex.Create(arr, ct))
            .WithTrackingName("GeneratedServiceNames");

        results = results
            .Combine(generatedServiceNames)
            .Select(static (pair, ct) =>
                GeneratedServiceCollisionValidator.Apply(pair.Left, pair.Right, ct))
            .WithTrackingName("GeneratedServiceValidatedServiceResults");

        var activeServiceIdentities = results
            .Where(static r => r.Model is not null)
            .Select(static (r, _) => ServiceIdentity.From(r))
            .WithTrackingName("WireServiceNameInputs");

        var wireServiceNames = activeServiceIdentities
            .Collect()
            .Select(static (arr, ct) => ServiceWireNameIndex.Create(arr, ct))
            .WithTrackingName("WireServiceNames");

        results = results
            .Combine(wireServiceNames)
            .Select(static (pair, ct) =>
                ServiceWireNameCollisionValidator.Apply(pair.Left, pair.Right, ct))
            .WithTrackingName("WireNameValidatedServiceResults");

        var subServiceResults = ApplySubServiceAvailability(
            results,
            CreateRejectedServices(results, "RejectedServiceInputs", "RejectedServices"),
            "SubServiceValidatedServiceResults");

        var finalRejectionInputs = subServiceResults
            .Combine(existingTypes)
            .Select(static (pair, ct) => FinalRejectionInput.From(pair.Left, pair.Right, ct))
            .WithTrackingName("FinalRejectionInputs");

        var finalRejectedServices = finalRejectionInputs
            .Collect()
            .Select(static (inputs, ct) => FinalRejectedServiceResolver.Resolve(inputs, ct))
            .WithTrackingName("FinalRejectedServices");

        results = ApplySubServiceAvailability(
            subServiceResults,
            finalRejectedServices,
            "FinalSubServiceValidatedServiceResults");

        return ApplyAsyncSiblingTypeCollisions(results, existingTypes, "ServiceResults");
    }

    private static IncrementalValuesProvider<ServiceResult> ApplySubServiceAvailability(
        IncrementalValuesProvider<ServiceResult> results,
        IncrementalValueProvider<RejectedServiceIndex> rejectedServices,
        string trackingName) =>
        results
            .Combine(rejectedServices)
            .Select(static (pair, ct) =>
                SubServiceAvailabilityValidator.Apply(pair.Left, pair.Right, ct))
            .WithTrackingName(trackingName);

    private static IncrementalValuesProvider<ServiceResult> ApplyAsyncSiblingTypeCollisions(
        IncrementalValuesProvider<ServiceResult> results,
        IncrementalValueProvider<ExistingTypeIndex> existingTypes,
        string trackingName) =>
        results
            .Combine(existingTypes)
            .Select(static (pair, ct) =>
                GeneratedTypeCollisionValidator.ApplyAsyncSibling(pair.Left, pair.Right, ct))
            .WithTrackingName(trackingName);

    private static IncrementalValueProvider<RejectedServiceIndex> CreateRejectedServices(
        IncrementalValuesProvider<ServiceResult> results,
        string inputTrackingName,
        string indexTrackingName)
    {
        var rejectedServiceIdentities = results
            .Select(static (r, _) => RejectedServiceIdentity.From(r))
            .Where(static rejected => rejected is not null)
            .Select(static (rejected, _) => rejected!.Value)
            .WithTrackingName(inputTrackingName);

        return rejectedServiceIdentities
            .Collect()
            .Select(static (arr, ct) => RejectedServiceIndex.Create(arr, ct))
            .WithTrackingName(indexTrackingName);
    }
}
