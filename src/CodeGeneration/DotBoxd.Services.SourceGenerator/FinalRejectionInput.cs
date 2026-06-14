using System.Threading;

namespace DotBoxd.Services.SourceGenerator;

internal readonly record struct FinalRejectionInput(
    string Namespace,
    string InterfaceName,
    string QualifiedInterfaceName,
    bool IsRejected,
    EquatableArray<FinalRejectionMethod> Methods)
{
    public static FinalRejectionInput From(
        ServiceResult result,
        ExistingTypeIndex existingTypes,
        CancellationToken ct)
    {
        if (result.Model is null)
        {
            return new FinalRejectionInput(
                string.Empty,
                string.Empty,
                result.QualifiedInterfaceName,
                IsRejected: RejectedServiceIdentity.From(result) is not null,
                EquatableArray<FinalRejectionMethod>.Empty);
        }

        var methods = HasPotentialAsyncSiblingCollision(result.Model, existingTypes, ct)
            ? CreateMethods(result.Model, ct)
            : EquatableArray<FinalRejectionMethod>.Empty;

        return new FinalRejectionInput(
            result.Model.Namespace,
            result.Model.InterfaceName,
            result.QualifiedInterfaceName,
            IsRejected: false,
            methods);
    }

    private static EquatableArray<FinalRejectionMethod> CreateMethods(
        ServiceModel model,
        CancellationToken ct)
    {
        var methods = new FinalRejectionMethod[model.Methods.Count];
        for (var i = 0; i < model.Methods.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            methods[i] = FinalRejectionMethod.From(model.Methods[i], ct);
        }

        return methods.ToEquatableArray();
    }

    private static bool HasPotentialAsyncSiblingCollision(
        ServiceModel model,
        ExistingTypeIndex existingTypes,
        CancellationToken ct) =>
        NamingHelpers.CanGenerateAsyncSiblingInterface(model.InterfaceName) &&
        existingTypes.Contains(
            new ExistingTypeKey(
                model.Namespace,
                NamingHelpers.AsyncSiblingInterfaceName(model.InterfaceName),
                0),
            ct);
}

internal readonly record struct FinalRejectionMethod(
    string OriginalSignatureKey,
    string CandidateSignatureKey,
    bool RequiresExtraProxyMethod,
    bool IsUnsupported,
    string? SubServiceQualifiedInterfaceName)
{
    public static FinalRejectionMethod From(MethodModel method, CancellationToken ct)
    {
        var originalKey = MethodSignatureFacts.GetSignatureKey(
            method.Name,
            method.TypeParameterCount,
            method.Parameters,
            ct);
        var candidateName = NamingHelpers.IsAsync(method.ReturnKind)
            ? method.Name
            : NamingHelpers.AsyncSiblingMethodName(method.Name);
        var candidateKey = MethodSignatureFacts.GetSignatureKey(
            candidateName,
            method.TypeParameterCount,
            FinalRejectionMethodParameters.Build(method, ct),
            ct);

        return new FinalRejectionMethod(
            originalKey,
            candidateKey,
            RequiresExtraProxyMethod: !(NamingHelpers.IsAsync(method.ReturnKind) &&
                method.HasCancellationToken),
            IsUnsupported: method.UnsupportedReason is not null,
            method.SubService?.QualifiedInterfaceName);
    }
}
