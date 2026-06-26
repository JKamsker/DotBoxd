using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed partial class InvokeAsyncCallShape
{
    private InvokeAsyncCallShape(
        BlockSyntax block,
        ITypeSymbol worldType,
        ITypeSymbol returnType,
        ITypeSymbol? captureType,
        bool usesReflectionCaptures,
        string parametersJson,
        string returnTypeJson,
        string argumentsExpression,
        IReadOnlyList<ITypeSymbol> argumentTypes,
        EquatableArray<InvokeAsyncSyncOut> syncOuts,
        IReadOnlyList<(string Name, ExpressionSyntax Value)> leadingLocals,
        Func<AssignmentExpressionSyntax, Func<ExpressionSyntax, string>, string?>? assignmentOverride,
        Func<ExpressionSyntax, string?>? expressionOverride)
    {
        Block = block;
        WorldType = worldType;
        ReturnType = returnType;
        CaptureType = captureType;
        UsesReflectionCaptures = usesReflectionCaptures;
        ParametersJson = parametersJson;
        ReturnTypeJson = returnTypeJson;
        ArgumentsExpression = argumentsExpression;
        ArgumentTypes = argumentTypes;
        SyncOuts = syncOuts;
        LeadingLocals = leadingLocals;
        AssignmentOverride = assignmentOverride;
        ExpressionOverride = expressionOverride;
    }

    public BlockSyntax Block { get; }

    public ITypeSymbol WorldType { get; }

    public ITypeSymbol ReturnType { get; }

    public ITypeSymbol? CaptureType { get; }

    public bool UsesReflectionCaptures { get; }

    public string ParametersJson { get; }

    public string ReturnTypeJson { get; }

    public string ArgumentsExpression { get; }

    public IReadOnlyList<ITypeSymbol> ArgumentTypes { get; }

    public EquatableArray<InvokeAsyncSyncOut> SyncOuts { get; }

    private IReadOnlyList<(string Name, ExpressionSyntax Value)> LeadingLocals { get; }

    private Func<AssignmentExpressionSyntax, Func<ExpressionSyntax, string>, string?>? AssignmentOverride { get; }

    private Func<ExpressionSyntax, string?>? ExpressionOverride { get; }

    public static InvokeAsyncCallShape? Create(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (method.TypeArguments.Length == 1 &&
            TrySingleLambdaArgument(arguments, out var lambdaExpression) &&
            InvokeAsyncLambdaShape.TryLambda(lambdaExpression, out var lambda) &&
            lambda.Body is BlockSyntax block &&
            InvokeAsyncLambdaShape.TryWorldParameter(lambda, model, cancellationToken, out var worldType))
        {
            return LambdaOnly(lambda, block, worldType, method.TypeArguments[0], model);
        }

        if (method.TypeArguments.Length == 2 &&
            TryCaptureArguments(arguments, out var capturesExpression, out lambdaExpression) &&
            InvokeAsyncLambdaShape.TryLambda(lambdaExpression, out lambda) &&
            lambda.Body is BlockSyntax captureBlock &&
            InvokeAsyncLambdaShape.TryCaptureParameter(
                lambda,
                model,
                capturesExpression,
                cancellationToken,
                out var captureParameter,
                out worldType) &&
            !HasExternalCaptures(lambda, model))
        {
            return CaptureBag(method.TypeArguments[1], captureParameter, captureBlock, model, worldType);
        }

        return null;
    }

    public static InvokeAsyncCallShape? Create(
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol generatedWorldType,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (TrySingleLambdaArgument(arguments, out var lambdaExpression) &&
            InvokeAsyncLambdaShape.TryLambda(lambdaExpression, out var lambda) &&
            lambda.Body is BlockSyntax block &&
            InvokeAsyncLambdaShape.TryWorldParameter(lambda, model, cancellationToken, generatedWorldType, out var worldType) &&
            TryGeneratedReceiverReturnType(invocation, block, model, cancellationToken, expectedTypeArgumentCount: 1, typeArgumentIndex: 0, out var returnType))
        {
            return LambdaOnly(lambda, block, worldType, returnType, model);
        }

        if (TryCaptureArguments(arguments, out var capturesExpression, out lambdaExpression) &&
            InvokeAsyncLambdaShape.TryLambda(lambdaExpression, out lambda) &&
            lambda.Body is BlockSyntax captureBlock &&
            InvokeAsyncLambdaShape.TryCaptureParameter(
                lambda,
                model,
                capturesExpression,
                cancellationToken,
                generatedWorldType,
                out var captureParameter,
                out worldType) &&
            TryGeneratedReceiverReturnType(invocation, captureBlock, model, cancellationToken, expectedTypeArgumentCount: 2, typeArgumentIndex: 1, out returnType) &&
            !HasExternalCaptures(lambda, model))
        {
            return CaptureBag(returnType, captureParameter, captureBlock, model, worldType);
        }

        return null;
    }

    private static bool TryGeneratedReceiverReturnType(
        InvocationExpressionSyntax invocation,
        BlockSyntax block,
        SemanticModel model,
        CancellationToken cancellationToken,
        int expectedTypeArgumentCount,
        int typeArgumentIndex,
        out ITypeSymbol returnType)
    {
        if (TryExplicitGenericTypeArgument(
                invocation,
                model,
                cancellationToken,
                expectedTypeArgumentCount,
                typeArgumentIndex,
                out returnType))
        {
            return true;
        }

        return InvokeAsyncLambdaShape.TryReturnType(block, model, cancellationToken, out returnType);
    }

    private static bool TryExplicitGenericTypeArgument(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        int expectedTypeArgumentCount,
        int typeArgumentIndex,
        out ITypeSymbol type)
    {
        type = null!;
        if (GenericInvokeAsyncName(invocation.Expression) is not { } generic ||
            generic.TypeArgumentList.Arguments.Count != expectedTypeArgumentCount)
        {
            return false;
        }

        var candidate = model.GetTypeInfo(
            generic.TypeArgumentList.Arguments[typeArgumentIndex],
            cancellationToken).Type;
        if (candidate is null || candidate.TypeKind == TypeKind.Error)
        {
            return false;
        }

        type = candidate;
        return true;
    }

    private static GenericNameSyntax? GenericInvokeAsyncName(ExpressionSyntax expression)
        => expression switch
        {
            GenericNameSyntax { Identifier.ValueText: "InvokeAsync" } generic => generic,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier.ValueText: "InvokeAsync" } generic } => generic,
            _ => null,
        };

    public string LowerBody(DotBoxDRpcJsonLowerer lowerer, BlockSyntax block)
        => lowerer.LowerBody(
            block,
            LeadingLocals,
            ReturnLocalNames(),
            ReturnTypeJsonForBody(),
            AssignmentOverride,
            ExpressionOverride);

    private static InvokeAsyncCallShape NoCapture(BlockSyntax block, ITypeSymbol worldType, ITypeSymbol returnType)
        => new(
            block,
            worldType,
            returnType,
            captureType: null,
            usesReflectionCaptures: false,
            parametersJson: "[]",
            returnTypeJson: DotBoxDRpcReturnType.JsonType(returnType),
            argumentsExpression: "global::System.Array.Empty<global::DotBoxD.Plugins.KernelRpcValue>()",
            argumentTypes: [],
            default,
            [],
            assignmentOverride: null,
            expressionOverride: null);

    private static InvokeAsyncCallShape CaptureBag(
        ITypeSymbol returnType,
        InvokeAsyncCaptureParameter captureParameter,
        BlockSyntax block,
        SemanticModel model,
        ITypeSymbol worldType)
    {
        var syncOuts = FindSyncOuts(block, captureParameter, model);
        var returnTypeJson = BuildReturnTypeJson(returnType, syncOuts);
        return new InvokeAsyncCallShape(
            block,
            worldType,
            returnType,
            captureParameter.Type,
            usesReflectionCaptures: false,
            CaptureParametersJson(captureParameter),
            returnTypeJson,
            CaptureArgumentsExpression(captureParameter.Type),
            [captureParameter.Type],
            new EquatableArray<InvokeAsyncSyncOut>(syncOuts),
            CreateLeadingLocals(syncOuts),
            (assignment, lower) => LowerCaptureAssignment(assignment, captureParameter, syncOuts, lower),
            expression => LowerCaptureRead(expression, captureParameter, syncOuts));
    }

    private IReadOnlyList<string> ReturnLocalNames()
    {
        var names = new string[SyncOuts.Count];
        for (var i = 0; i < SyncOuts.Count; i++)
        {
            names[i] = SyncOuts[i].LocalName;
        }

        return names;
    }

    private string? ReturnTypeJsonForBody()
        => SyncOuts.Count == 0 ? null : ReturnTypeJson;

    private static bool HasExternalCaptures(LambdaExpressionSyntax lambda, SemanticModel model)
        => ImplicitCaptureSet.Create(lambda, model) is { HasExternalCaptures: true };

    private static string BuildReturnTypeJson(ITypeSymbol returnType, IReadOnlyList<InvokeAsyncSyncOut> syncOuts)
    {
        if (syncOuts.Count == 0)
        {
            return DotBoxDRpcReturnType.JsonType(returnType);
        }

        var fields = new string[1 + syncOuts.Count];
        fields[0] = DotBoxDRpcReturnType.JsonType(returnType);
        for (var i = 0; i < syncOuts.Count; i++)
        {
            fields[i + 1] = DotBoxDRpcTypeMapper.JsonType(syncOuts[i].Type);
        }

        return "{\"name\":\"Record\",\"arguments\":[" + string.Join(",", fields) + "]}";
    }
}

internal sealed record InvokeAsyncCaptureParameter(string Name, INamedTypeSymbol Type);

internal sealed record InvokeAsyncSyncOut(
    string TargetName,
    ITypeSymbol Type,
    string LocalName,
    ExpressionSyntax? Initializer);
