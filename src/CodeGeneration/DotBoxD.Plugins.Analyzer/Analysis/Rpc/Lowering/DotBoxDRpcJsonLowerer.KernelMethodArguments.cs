using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    private static Dictionary<string, int> ParameterOrdinals(IReadOnlyList<IParameterSymbol> parameters)
    {
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < parameters.Count; i++)
            ordinals.Add(parameters[i].Name, i);

        return ordinals;
    }

    private static IEnumerable<BoundKernelMethodArgument> ArgumentsInEvaluationOrder(BoundKernelMethodCall call)
    {
        foreach (var argument in call.EvaluationOrder)
        {
            yield return argument;
        }

        foreach (var argument in call.Arguments)
        {
            if (argument.UsesDefault)
            {
                yield return argument;
            }
        }
    }
}
