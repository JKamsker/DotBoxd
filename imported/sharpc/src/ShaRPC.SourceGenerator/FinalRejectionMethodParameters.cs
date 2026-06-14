using System.Collections.Generic;
using System.Threading;

namespace ShaRPC.SourceGenerator;

internal static class FinalRejectionMethodParameters
{
    public static EquatableArray<ParameterModel> Build(MethodModel method, CancellationToken ct)
    {
        if (NamingHelpers.IsAsync(method.ReturnKind) && method.HasCancellationToken)
        {
            return method.Parameters;
        }

        var parameters = new List<ParameterModel>();
        foreach (var parameter in method.Parameters.Array)
        {
            ct.ThrowIfCancellationRequested();

            if (!parameter.IsCancellationToken)
            {
                parameters.Add(parameter);
            }
        }

        parameters.Add(new ParameterModel(
            "ct",
            "global::System.Threading.CancellationToken",
            "global::System.Threading.CancellationToken",
            IsCancellationToken: true,
            HasDefaultValue: true,
            MetadataType: "global::System.Threading.CancellationToken"));

        return parameters.ToEquatableArray();
    }
}
