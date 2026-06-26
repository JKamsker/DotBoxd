using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed partial class InvokeAsyncCallShape
{
    private static InvokeAsyncCallShape? LambdaOnly(
        LambdaExpressionSyntax lambda,
        BlockSyntax block,
        ITypeSymbol worldType,
        ITypeSymbol returnType,
        SemanticModel model)
    {
        if (ImplicitCaptureSet.Create(lambda, model) is not { } captures)
        {
            return null;
        }

        return captures.HasExternalCaptures
            ? FromImplicitCaptures(block, worldType, InvokeAsyncLambdaShape.WorldParameterName(lambda), returnType, captures)
            : NoCapture(block, worldType, InvokeAsyncLambdaShape.WorldParameterName(lambda), returnType);
    }

    private static InvokeAsyncCallShape FromImplicitCaptures(
        BlockSyntax block,
        ITypeSymbol worldType,
        string worldParameterName,
        ITypeSymbol returnType,
        ImplicitCaptureSet captures)
    {
        var syncOuts = captures.SyncOuts
            .Select(capture => new InvokeAsyncSyncOut(capture.Name, capture.Type, capture.Name, Initializer: null))
            .ToArray();
        return new InvokeAsyncCallShape(
            block,
            worldType,
            worldParameterName,
            returnType,
            captureType: null,
            usesReflectionCaptures: true,
            ImplicitParametersJson(captures.All),
            BuildReturnTypeJson(returnType, syncOuts),
            ImplicitArgumentsExpression(captures.All),
            captures.All.Select(static capture => capture.Type).ToArray(),
            new EquatableArray<InvokeAsyncSyncOut>(syncOuts),
            [],
            assignmentOverride: null,
            expressionOverride: null);
    }

    private static string ImplicitParametersJson(IReadOnlyList<ImplicitCapture> captures)
    {
        var parameters = new string[captures.Count];
        for (var i = 0; i < captures.Count; i++)
        {
            parameters[i] = "{\"name\":" + DotBoxDRpcJsonLowerer.Str(captures[i].Name) +
                            ",\"type\":" + DotBoxDRpcTypeMapper.JsonType(captures[i].Type) + "}";
        }

        return "[" + string.Join(",", parameters) + "]";
    }

    private static string ImplicitArgumentsExpression(IReadOnlyList<ImplicitCapture> captures)
    {
        var arguments = new string[captures.Count];
        for (var i = 0; i < captures.Count; i++)
        {
            arguments[i] = InvokeAsyncArgumentWriterSource.WriteExpression(
                captures[i].Type,
                "__ReadCapture<" + TypeName(captures[i].Type) + ">(lambda, " +
                DotBoxDRpcJsonLowerer.Str(captures[i].Name) + ")");
        }

        return "new global::DotBoxD.Plugins.KernelRpcValue[] { " + string.Join(", ", arguments) + " }";
    }

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private sealed record ImplicitCaptureSet(
        IReadOnlyList<ImplicitCapture> All,
        IReadOnlyList<ImplicitCapture> SyncOuts)
    {
        public bool HasExternalCaptures => All.Count > 0;

        public static ImplicitCaptureSet? Create(LambdaExpressionSyntax lambda, SemanticModel model)
        {
            if (lambda.Body is not BlockSyntax block ||
                model.AnalyzeDataFlow(block) is not { Succeeded: true } flow)
            {
                return null;
            }

            var lambdaParameters = LambdaParameters(lambda, model);
            var declaredInside = flow.VariablesDeclared;
            var all = new List<ImplicitCapture>();
            var syncOuts = new List<ImplicitCapture>();

            foreach (var symbol in flow.DataFlowsIn)
            {
                AddCapture(all, symbol, lambdaParameters, declaredInside);
            }

            foreach (var symbol in flow.WrittenInside)
            {
                if (AddCapture(all, symbol, lambdaParameters, declaredInside) is { } capture &&
                    !Contains(syncOuts, symbol))
                {
                    syncOuts.Add(capture);
                }
            }

            ValidateImplicitCaptureMutations(block, all, model);
            return new ImplicitCaptureSet(all, syncOuts);
        }

        private static ISymbol?[] LambdaParameters(LambdaExpressionSyntax lambda, SemanticModel model)
            => lambda switch
            {
                ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters
                    .Select(parameter => model.GetDeclaredSymbol(parameter))
                    .Where(static symbol => symbol is not null)
                    .ToArray(),
                SimpleLambdaExpressionSyntax simple => [model.GetDeclaredSymbol(simple.Parameter)],
                _ => []
            };

        private static ImplicitCapture? AddCapture(
            ICollection<ImplicitCapture> captures,
            ISymbol symbol,
            IReadOnlyList<ISymbol?> lambdaParameters,
            IReadOnlyList<ISymbol> declaredInside)
        {
            if (IsLocalCapture(symbol, lambdaParameters, declaredInside) is not { } capture ||
                Contains(captures, symbol))
            {
                return null;
            }

            captures.Add(capture);
            return capture;
        }

        private static ImplicitCapture? IsLocalCapture(
            ISymbol symbol,
            IReadOnlyList<ISymbol?> lambdaParameters,
            IReadOnlyList<ISymbol> declaredInside)
        {
            if (lambdaParameters.Any(parameter => SymbolEqualityComparer.Default.Equals(parameter, symbol)) ||
                declaredInside.Any(declared => SymbolEqualityComparer.Default.Equals(declared, symbol)))
            {
                return null;
            }

            return symbol switch
            {
                ILocalSymbol local => new ImplicitCapture(local.Name, local.Type, local),
                IParameterSymbol { IsThis: false } parameter => new ImplicitCapture(parameter.Name, parameter.Type, parameter),
                _ => null
            };
        }

        private static bool Contains(IEnumerable<ImplicitCapture> captures, ISymbol symbol)
            => captures.Any(capture => SymbolEqualityComparer.Default.Equals(capture.Symbol, symbol));
    }

    private sealed record ImplicitCapture(string Name, ITypeSymbol Type, ISymbol Symbol);
}
