using DotBoxD.Plugins.Analyzer.Analysis.HookChains;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.MergeableIr;

internal static class MergeableIrStepModelFactory
{
    private const string CurrentValueName = "$dotboxd.current";

    public static MergeableIrStepCreateResult? Create(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return null;
        }

        // TryRead throws NotSupportedException for malformed *marked* calls (wrong delegate type,
        // filter not returning bool, out-of-range kind). Keep it inside the try so those surface as a
        // PluginKernelDiagnostic instead of escaping and crashing the incremental generator.
        try
        {
            if (MergeableIrMarkedCallReader.TryRead(invocation, context.SemanticModel, cancellationToken) is not { } call)
            {
                return null;
            }

            return new MergeableIrStepCreateResult(
                Create(invocation, context.SemanticModel, call, cancellationToken),
                null);
        }
        catch (NotSupportedException ex)
        {
            return new MergeableIrStepCreateResult(
                null,
                new PluginKernelDiagnostic(
                    "[LowerToIr] step could not be lowered: " + ex.Message,
                    PluginDiagnosticLocation.From(invocation.GetLocation())));
        }
    }

    private static MergeableIrStepModel Create(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        MergeableIrMarkedLoweringCall call,
        CancellationToken cancellationToken)
    {
        if (call.Argument.Expression is not LambdaExpressionSyntax { ExpressionBody: { } body } lambda)
        {
            throw new NotSupportedException("the marked argument must be an expression-bodied lambda.");
        }

        var parameterName = LambdaParameterName(lambda);
        var inputType = call.InputType;
        var inputTypeSource = SandboxTypeSourceEmitter.TryEmit(inputType)
            ?? throw new NotSupportedException("the input type is not wire-eligible.");
        var inputTag = SandboxTypeSourceEmitter.ManifestTag(inputType);

        // An anonymous-type projection (e.g. Select(e => new { e.TargetId })) has no C#-nameable type, so its
        // display string cannot be emitted as interceptor source. Reject it as a diagnostic instead of
        // generating code that fails to compile.
        if (call.OutputType.IsAnonymousType)
        {
            throw new NotSupportedException(
                "anonymous-type projections are not supported; project to a named type so the generated interceptor can name it.");
        }

        var outputTag = OutputTag(call.Kind, call.OutputType);
        var capabilities = new SortedSet<string>(StringComparer.Ordinal);
        var effects = new SortedSet<string>(StringComparer.Ordinal);
        AddInputPropertyCapabilities(inputType, capabilities);

        var current = new DotBoxDExpressionModel(
            $"{DotBoxDGenerationNames.Helpers.Var}({LiteralReader.StringLiteral(CurrentValueName)})",
            inputTag,
            false);
        var loweringContext = new DotBoxDExpressionLoweringContext(
            eventParameterName: string.Empty,
            eventProperties: default,
            liveSettings: default,
            model,
            cancellationToken,
            projectedElementName: parameterName,
            projectedElement: current,
            projectedElementType: inputType,
            capabilities: capabilities,
            effects: effects);
        var value = DotBoxDExpressionModelFactory.Create(body, loweringContext);
        ValidateOutput(call.Kind, outputTag, value);

        var id = MergeableIrStepIdentity.Compute(invocation);
        var ns = HookChainIdentity.Namespace(invocation);
        var className = "LoweredPipelineStep_" + id;
        var fullName = string.IsNullOrEmpty(ns)
            ? DotBoxDGenerationNames.TypeNames.GlobalPrefix + className
            : DotBoxDGenerationNames.TypeNames.GlobalPrefix + ns + "." + className;

        var interception = Interception(invocation, model, call, fullName, cancellationToken);
        return new MergeableIrStepModel(
            HintName(ns, className),
            ns,
            className,
            call.Kind.ToString(),
            inputTag,
            outputTag,
            $"new {DotBoxDGenerationNames.TypeNames.GlobalParameter}({LiteralReader.StringLiteral(CurrentValueName)}, {inputTypeSource})",
            value.Source,
            EquatableArray<string>.FromOwned([.. capabilities]),
            EquatableArray<string>.FromOwned([.. effects]),
            interception);
    }

    private static MergeableIrStepInterception Interception(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        MergeableIrMarkedLoweringCall call,
        string stepFullName,
        CancellationToken cancellationToken)
    {
        var location = model.GetInterceptableLocation(invocation, cancellationToken)
            ?? throw new NotSupportedException("the call site is not interceptable.");
        var method = call.Method;
        var receiverType = ReceiverType(invocation, model, cancellationToken)
            ?? throw new NotSupportedException("the marked method must be called on an instance receiver.");
        if (!HasStepOverload(receiverType, method, model.Compilation))
        {
            throw new NotSupportedException(
                $"receiver type '{receiverType.Name}' must expose a '{method.Name}(LoweredPipelineStep)' overload.");
        }

        return new MergeableIrStepInterception(
            location.GetInterceptsLocationAttributeSyntax(),
            receiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            call.Parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            method.Name,
            MethodTypeArguments(method),
            stepFullName);
    }

    private static string LambdaParameterName(LambdaExpressionSyntax lambda)
        => lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized =>
                parenthesized.ParameterList.Parameters[0].Identifier.ValueText,
            _ => throw new NotSupportedException("the lambda must have exactly one parameter.")
        };

    private static string OutputTag(MergeableIrLoweredStepKind kind, ITypeSymbol outputType)
        => kind == MergeableIrLoweredStepKind.Filter
            ? DotBoxDGenerationNames.ManifestTypes.Bool
            : SandboxTypeSourceEmitter.ManifestTag(outputType);

    private static void ValidateOutput(
        MergeableIrLoweredStepKind kind,
        string outputTag,
        DotBoxDExpressionModel value)
    {
        if (kind == MergeableIrLoweredStepKind.Filter &&
            !string.Equals(value.Type, DotBoxDGenerationNames.ManifestTypes.Bool, StringComparison.Ordinal))
        {
            throw new NotSupportedException("filter expressions must lower to bool.");
        }

        if (kind == MergeableIrLoweredStepKind.Projection &&
            string.Equals(outputTag, DotBoxDGenerationNames.ManifestTypes.Unsupported, StringComparison.Ordinal))
        {
            throw new NotSupportedException("the projection output type is not wire-eligible.");
        }
    }

    private static INamedTypeSymbol? ReceiverType(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
        => invocation.Expression is MemberAccessExpressionSyntax member &&
           model.GetTypeInfo(member.Expression, cancellationToken).Type is INamedTypeSymbol receiver
            ? receiver
            : null;

    private static bool HasStepOverload(
        INamedTypeSymbol receiverType,
        IMethodSymbol original,
        Compilation compilation)
    {
        for (INamedTypeSymbol? current = receiverType; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(original.Name).OfType<IMethodSymbol>())
            {
                if (SymbolEqualityComparer.Default.Equals(member, original) ||
                    member.TypeParameters.Length != original.TypeParameters.Length ||
                    member.Parameters.Length != 1 ||
                    !IsLoweredPipelineStep(member.Parameters[0].Type, compilation))
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static bool IsLoweredPipelineStep(ITypeSymbol type, Compilation compilation)
        => compilation.GetTypeByMetadataName(MergeableIrContractNames.LoweredPipelineStep) is { } expected &&
           SymbolEqualityComparer.Default.Equals(type, expected);

    private static string MethodTypeArguments(IMethodSymbol method)
    {
        if (!method.IsGenericMethod)
        {
            return string.Empty;
        }

        return "<" + string.Join(", ", method.TypeArguments.Select(static type =>
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) + ">";
    }

    private static void AddInputPropertyCapabilities(ITypeSymbol inputType, ISet<string> capabilities)
    {
        if (inputType is not INamedTypeSymbol named)
        {
            return;
        }

        foreach (var property in named.GetMembers().OfType<IPropertySymbol>())
        {
            foreach (var attribute in property.GetAttributes())
            {
                if (string.Equals(
                        attribute.AttributeClass?.ToDisplayString(),
                        DotBoxDMetadataNames.CapabilityAttribute,
                        StringComparison.Ordinal) &&
                    attribute.ConstructorArguments.Length == 1 &&
                    attribute.ConstructorArguments[0].Value is string capability &&
                    !string.IsNullOrEmpty(capability))
                {
                    capabilities.Add(capability);
                }
            }
        }
    }

    private static string HintName(string ns, string className)
        => string.IsNullOrEmpty(ns)
            ? className + ".g.cs"
            : ns.Replace(DotBoxDGenerationNames.CSharpIdentifiers.EscapePrefix, string.Empty) + "." + className + ".g.cs";

}
