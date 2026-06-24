using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class GeneratedKernelMethodDescriptorFactory
{
    public static GeneratedKernelMethodDescriptorModel[] Create(
        INamedTypeSymbol contextType,
        ITypeSymbol worldType,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var descriptors = new List<GeneratedKernelMethodDescriptorModel>();
        foreach (var method in contextType.GetMembers().OfType<IMethodSymbol>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HasKernelMethodAttribute(method))
            {
                continue;
            }

            descriptors.Add(Create(contextType, worldType, compilation, method, cancellationToken));
        }

        return descriptors.ToArray();
    }

    private static GeneratedKernelMethodDescriptorModel Create(
        INamedTypeSymbol contextType,
        ITypeSymbol worldType,
        Compilation compilation,
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        if (method.IsStatic ||
            method.IsGenericMethod ||
            method.MethodKind != MethodKind.Ordinary)
        {
            throw new NotSupportedException(
                $"Context [KernelMethod] helper '{method.Name}' must be a non-generic instance method.");
        }

        var returnType = DotBoxDTypeNameReader.SandboxTypeName(method.ReturnType);
        if (string.Equals(returnType, DotBoxDGenerationNames.ManifestTypes.Unsupported, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Context [KernelMethod] helper '{method.Name}' must return a supported scalar type.");
        }

        var bindings = BindParameters(method);
        var body = KernelMethodBody(method, cancellationToken);
        var bodyModel = compilation.GetSemanticModel(body.SyntaxTree);
        var capabilities = new SortedSet<string>(StringComparer.Ordinal);
        var effects = new SortedSet<string>(StringComparer.Ordinal);
        var context = new DotBoxDExpressionLoweringContext(
            eventParameterName: string.Empty,
            eventProperties: default,
            liveSettings: default,
            bodyModel,
            cancellationToken,
            serverContextType: contextType,
            contextWorldType: worldType,
            capabilities: capabilities,
            effects: effects,
            inlinedBindings: bindings);
        var lowered = DotBoxDExpressionModelFactory.Create(body, context);
        if (!string.Equals(lowered.Type, returnType, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Context [KernelMethod] helper '{method.Name}' body lowered to {lowered.Type} but its return type is {returnType}.");
        }

        var payload = new KernelMethodDescriptorPayload(
            KernelMethodDescriptorPayload.CurrentVersion,
            TypeName(contextType),
            method.MetadataName,
            KernelMethodSignature.Create(method),
            returnType,
            lowered.Allocates,
            new EquatableArray<string>(capabilities.ToArray()),
            new EquatableArray<string>(effects.ToArray()),
            new EquatableArray<KernelMethodDescriptorParameter>(
                method.Parameters.Select(static (parameter, index) => new KernelMethodDescriptorParameter(
                    Placeholder(index),
                    DotBoxDTypeNameReader.SandboxTypeName(parameter.Type))).ToArray()),
            lowered.Source);
        var json = payload.ToJson();
        return new GeneratedKernelMethodDescriptorModel(
            TypeName(contextType),
            method.MetadataName,
            KernelMethodSignature.Create(method),
            KernelMethodDescriptorPayload.Hash(json),
            json);
    }

    private static IReadOnlyDictionary<string, DotBoxDExpressionModel> BindParameters(IMethodSymbol method)
    {
        var bindings = new Dictionary<string, DotBoxDExpressionModel>(StringComparer.Ordinal);
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];
            if (parameter.RefKind != RefKind.None)
            {
                throw new NotSupportedException(
                    $"Context [KernelMethod] helper '{method.Name}' parameters must be value parameters.");
            }

            var type = DotBoxDTypeNameReader.SandboxTypeName(parameter.Type);
            if (string.Equals(type, DotBoxDGenerationNames.ManifestTypes.Unsupported, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"Context [KernelMethod] helper '{method.Name}' parameters must use supported scalar types.");
            }

            bindings[parameter.Name] = new DotBoxDExpressionModel(Placeholder(i), type, Allocates: false);
        }

        return bindings;
    }

    private static ExpressionSyntax KernelMethodBody(IMethodSymbol method, CancellationToken cancellationToken)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration)
            {
                continue;
            }

            if (declaration.ExpressionBody?.Expression is { } expressionBody)
            {
                return expressionBody;
            }

            if (declaration.Body is { } block &&
                block.Statements.Count == 1 &&
                block.Statements[0] is ReturnStatementSyntax { Expression: { } returned })
            {
                return returned;
            }

            throw new NotSupportedException(
                $"Context [KernelMethod] helper '{method.Name}' must have an expression body or a single return statement.");
        }

        throw new NotSupportedException(
            $"Context [KernelMethod] helper '{method.Name}' must be declared in source to emit its descriptor.");
    }

    private static bool HasKernelMethodAttribute(IMethodSymbol method)
        => method.GetAttributes().Any(attribute => string.Equals(
            attribute.AttributeClass?.ToDisplayString(),
            DotBoxDMetadataNames.KernelMethodAttribute,
            StringComparison.Ordinal));

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Placeholder(int index)
        => "__dotboxd_kernel_method_arg_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "__";
}
