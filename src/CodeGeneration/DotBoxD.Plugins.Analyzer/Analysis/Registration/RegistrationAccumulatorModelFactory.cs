namespace DotBoxD.Plugins.Analyzer.Analysis.Registration;

using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class RegistrationAccumulatorModelFactory
{
    public const string TargetAttribute =
        "DotBoxD.Abstractions.GeneratePluginRegistrationAccumulatorAttribute";

    public const string RootAttribute =
        "DotBoxD.Abstractions.GeneratePluginRegistrationRootAccumulatorAttribute";

    private static readonly SymbolDisplayFormat FullyQualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat;

    public static RegistrationAccumulatorGenerationResult? CreateTarget(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TargetSymbol is not INamedTypeSymbol type ||
            context.TargetNode is not TypeDeclarationSyntax declaration)
        {
            return null;
        }

        try
        {
            EnsureTopLevel(type);
            var attribute = context.Attributes[0];
            var accumulatorName = RequiredStringArgument(attribute, 0, "accumulator name");
            var methodName = RequiredStringArgument(attribute, 1, "method name");
            ValidateIdentifier(accumulatorName, "Accumulator");
            ValidateIdentifier(methodName, "Registration method");
            ValidateGeneratedTypeName(type, accumulatorName);

            var method = ResolveRegistrationMethod(type, methodName, context.SemanticModel.Compilation);
            var typeParameters = TypeParameters(method);
            var model = new RegistrationAccumulatorTargetModel(
                Namespace(type),
                TypeName(type),
                accumulatorName,
                methodName,
                typeParameters,
                Location(declaration));
            return new RegistrationAccumulatorGenerationResult(model, null, null);
        }
        catch (NotSupportedException ex)
        {
            return Fail(declaration, ex.Message);
        }
    }

    public static RegistrationAccumulatorGenerationResult? CreateRoot(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TargetSymbol is not INamedTypeSymbol type ||
            context.TargetNode is not TypeDeclarationSyntax declaration)
        {
            return null;
        }

        try
        {
            EnsureTopLevel(type);
            var accumulatorName = RequiredStringArgument(context.Attributes[0], 0, "accumulator name");
            ValidateIdentifier(accumulatorName, "Accumulator");
            ValidateGeneratedTypeName(type, accumulatorName);
            var model = new RegistrationRootAccumulatorModel(
                Namespace(type),
                TypeName(type),
                accumulatorName,
                PublicInstanceProperties(type),
                Location(declaration));
            return new RegistrationAccumulatorGenerationResult(null, model, null);
        }
        catch (NotSupportedException ex)
        {
            return Fail(declaration, ex.Message);
        }
    }

    private static void EnsureTopLevel(INamedTypeSymbol type)
    {
        if (type.ContainingType is not null)
        {
            throw new NotSupportedException(
                $"Registration accumulator generation supports top-level control types; '{type.ToDisplayString()}' is nested.");
        }
    }

    private static string RequiredStringArgument(AttributeData attribute, int index, string name)
    {
        if (attribute.ConstructorArguments.Length <= index ||
            attribute.ConstructorArguments[index].Value is not string value ||
            string.IsNullOrWhiteSpace(value))
        {
            throw new NotSupportedException($"Registration accumulator {name} must be a non-empty string.");
        }

        return value;
    }

    private static void ValidateIdentifier(string value, string label)
    {
        if (!SyntaxFacts.IsValidIdentifier(value))
        {
            throw new NotSupportedException($"{label} name '{value}' is not a valid C# identifier.");
        }
    }

    private static IMethodSymbol ResolveRegistrationMethod(
        INamedTypeSymbol type,
        string methodName,
        Compilation compilation)
    {
        var methods = type.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Where(static method => method.MethodKind == MethodKind.Ordinary && !method.IsStatic)
            .ToArray();
        if (methods.Length != 1)
        {
            throw new NotSupportedException(
                $"Registration accumulator method '{methodName}' must resolve to exactly one instance method.");
        }

        var method = methods[0];
        if (method.Parameters.Length != 0)
        {
            throw new NotSupportedException(
                $"Registration accumulator method '{methodName}' must not declare parameters.");
        }

        if (!IsCallableFromGeneratedAccumulator(method))
        {
            throw new NotSupportedException(
                $"Registration accumulator method '{methodName}' must be accessible from generated accumulator code.");
        }

        if (!IsResultBearingAwaitableRegistrationReturn(method.ReturnType, compilation))
        {
            throw new NotSupportedException(
                $"Registration accumulator method '{methodName}' must return Task<T> or ValueTask<T>; " +
                $"'{method.ReturnType.ToDisplayString()}' has no result payload.");
        }

        return method;
    }

    private static bool IsResultBearingAwaitableRegistrationReturn(
        ITypeSymbol type,
        Compilation compilation)
        => DotBoxDWellKnownTaskTypes.IsGenericTask(type, compilation, out _) ||
           DotBoxDWellKnownTaskTypes.IsGenericValueTask(type, compilation, out _);

    private static bool IsCallableFromGeneratedAccumulator(IMethodSymbol method)
        => method.DeclaredAccessibility is Accessibility.Public
            or Accessibility.Internal
            or Accessibility.ProtectedOrInternal;

    private static EquatableArray<RegistrationTypeParameterModel> TypeParameters(IMethodSymbol method)
    {
        var models = new RegistrationTypeParameterModel[method.TypeParameters.Length];
        for (var i = 0; i < method.TypeParameters.Length; i++)
        {
            var parameter = method.TypeParameters[i];
            models[i] = new RegistrationTypeParameterModel(
                parameter.Name,
                new EquatableArray<string>(Constraints(parameter)));
        }

        return EquatableArray<RegistrationTypeParameterModel>.FromOwned(models);
    }

    private static IEnumerable<string> Constraints(ITypeParameterSymbol parameter)
    {
        if (parameter.HasUnmanagedTypeConstraint)
        {
            yield return "unmanaged";
        }
        else if (parameter.HasValueTypeConstraint)
        {
            yield return "struct";
        }
        else if (parameter.HasReferenceTypeConstraint)
        {
            yield return "class";
        }

        if (parameter.HasNotNullConstraint)
        {
            yield return "notnull";
        }

        foreach (var constraint in parameter.ConstraintTypes)
        {
            yield return TypeName(constraint);
        }

        if (parameter.HasConstructorConstraint)
        {
            yield return "new()";
        }
    }

    private static EquatableArray<RegistrationRootPropertyModel> PublicInstanceProperties(INamedTypeSymbol type)
    {
        var properties = new List<RegistrationRootPropertyModel>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (property is { IsStatic: false, DeclaredAccessibility: Accessibility.Public } &&
                    property.GetMethod is not null &&
                    property.Parameters.Length == 0)
                {
                    if (!seenNames.Add(property.Name))
                    {
                        continue;
                    }

                    properties.Add(new RegistrationRootPropertyModel(
                        property.Name,
                        TypeName(property.ContainingType),
                        RegistrationAssignableTypeNameCollector.Collect(property.Type)));
                }
            }
        }

        return EquatableArray<RegistrationRootPropertyModel>.FromOwned(properties.ToArray());
    }

    private static RegistrationAccumulatorGenerationResult Fail(TypeDeclarationSyntax declaration, string message)
        => new(null, null, PluginKernelDiagnostic.Create(declaration.Identifier, message));

    private static PluginDiagnosticLocation Location(TypeDeclarationSyntax declaration)
        => PluginDiagnosticLocation.From(declaration.Identifier.GetLocation());

    private static string Namespace(INamedTypeSymbol type)
        => type.ContainingNamespace.IsGlobalNamespace ? string.Empty : type.ContainingNamespace.ToDisplayString();

    private static string TypeName(ITypeSymbol type)
        => type is ITypeParameterSymbol parameter
            ? Identifier(parameter.Name)
            : type.ToDisplayString(FullyQualifiedFormat);

    private static string Identifier(string name)
    {
        var kind = SyntaxFacts.GetKeywordKind(name);
        if (kind == SyntaxKind.None)
        {
            kind = SyntaxFacts.GetContextualKeywordKind(name);
        }

        return kind == SyntaxKind.None ? name : "@" + name;
    }

    private static void ValidateGeneratedTypeName(INamedTypeSymbol receiverType, string generatedName)
    {
        foreach (var existing in receiverType.ContainingNamespace.GetTypeMembers(generatedName, 0))
        {
            throw new NotSupportedException(
                $"Generated registration accumulator type '{existing.Name}' collides with an existing type in namespace " +
                $"'{NamespaceDisplay(receiverType.ContainingNamespace)}'.");
        }
    }

    private static string NamespaceDisplay(INamespaceSymbol @namespace)
        => @namespace.IsGlobalNamespace ? "<global namespace>" : @namespace.ToDisplayString();
}
