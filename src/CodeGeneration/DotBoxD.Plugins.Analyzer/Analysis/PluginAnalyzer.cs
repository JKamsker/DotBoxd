using System.Collections.Immutable;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class PluginAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor ForbiddenHostApiRule = new(
        "DBXK001",
        "Forbidden host API is not allowed in plugin kernels",
        "Forbidden host API '{0}' is not allowed in this plugin contract",
        "DotBoxD.Kernels.Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Hook filters and kernel handlers must use approved safe facades instead of host APIs.",
        helpLinkUri: PluginAnalyzerDiagnostics.ShippedRulesHelpLinkBase + "DBXK001",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    public static readonly DiagnosticDescriptor LiveSettingTypeRule = new(
        "DBXK020",
        "Live setting type is not supported",
        "Live setting type '{0}' is not supported",
        "DotBoxD.Kernels.Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Live settings must use supported scalar types.",
        helpLinkUri: PluginAnalyzerDiagnostics.ShippedRulesHelpLinkBase + "DBXK020");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(
            ForbiddenHostApiRule,
            LiveSettingTypeRule,
            PluginAnalyzerDiagnostics.LocalContextMemberRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
        context.RegisterCompilationStartAction(startContext =>
        {
            var helperGraph = new ForbiddenHelperCallGraph();
            startContext.RegisterOperationAction(c => AnalyzeInvocation(c, helperGraph), OperationKind.Invocation);
            startContext.RegisterOperationAction(c => AnalyzeObjectCreation(c, helperGraph), OperationKind.ObjectCreation);
            startContext.RegisterOperationAction(c => AnalyzePropertyReference(c, helperGraph), OperationKind.PropertyReference);
            startContext.RegisterOperationAction(c => AnalyzeFieldReference(c, helperGraph), OperationKind.FieldReference);
            startContext.RegisterOperationAction(c => AnalyzeTypeOf(c, helperGraph), OperationKind.TypeOf);
            startContext.RegisterOperationAction(c => AnalyzeMethodReference(c, helperGraph), OperationKind.MethodReference);
            startContext.RegisterCompilationEndAction(helperGraph.ReportDiagnostics);
        });
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (!HasAttribute(method, DotBoxDMetadataNames.LocalAttribute))
        {
            return;
        }

        ValidateLocalMember(context, method, method);
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        if (HasAttribute(property, DotBoxDMetadataNames.LocalAttribute))
        {
            ValidateLocalMember(context, property, property);
        }

        if (!HasAttribute(property, DotBoxDMetadataNames.LiveSettingAttribute))
        {
            return;
        }

        if (!IsAllowedLiveSettingType(property.Type))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                LiveSettingTypeRule,
                property.Locations.FirstOrDefault(),
                property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            ReportForbiddenInInitializer(context, invocation.TargetMethod.ContainingType);
            RecordInitializerRootCall(context, helperGraph, invocation.TargetMethod);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, invocation.TargetMethod.ContainingType);
        helperGraph.RecordCall(method, invocation.TargetMethod, context.Operation.Syntax.GetLocation());
        ReportLocalUseIfInvalid(context, invocation.TargetMethod);
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            ReportForbiddenInInitializer(context, creation.Type);
            if (creation.Constructor is { } initializerConstructor)
            {
                RecordInitializerRootCall(context, helperGraph, initializerConstructor);
            }

            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, creation.Type);
        if (creation.Constructor is { } constructor)
        {
            helperGraph.RecordCall(method, constructor, context.Operation.Syntax.GetLocation());
        }
    }

    private static void AnalyzePropertyReference(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var property = ((IPropertyReferenceOperation)context.Operation).Property;
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            ReportForbiddenInInitializer(context, property.ContainingType);
            RecordInitializerPropertyRootCall(context, helperGraph, property);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, property.ContainingType);
        ReportLocalUseIfInvalid(context, property);

        // A forbidden API reached through a helper property's accessor body is only linked to the kernel
        // if we record an edge to the accessor it actually uses: the getter for a read, the setter for a
        // write, both for a compound/increment. Without this the accessor taints but never reaches a root.
        var (usesGetter, usesSetter) = AccessorUsage(context.Operation);
        var location = context.Operation.Syntax.GetLocation();
        if (usesGetter && property.GetMethod is { } getter)
        {
            helperGraph.RecordCall(method, getter, location);
        }

        if (usesSetter && property.SetMethod is { } setter)
        {
            helperGraph.RecordCall(method, setter, location);
        }
    }

    private static void AnalyzeFieldReference(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var field = ((IFieldReferenceOperation)context.Operation).Field;
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            ReportForbiddenInInitializer(context, field.ContainingType);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, field.ContainingType);
    }

    private static void AnalyzeTypeOf(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var type = ((ITypeOfOperation)context.Operation).Type;
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            ReportForbiddenInInitializer(context, type);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, type);
    }

    // A method group / delegate reference to a helper (e.g. items.Select(Helper.Danger)) is an
    // IMethodReferenceOperation, not an invocation, so without this it never adds a caller -> helper edge
    // and a forbidden API in the referenced helper escapes the call graph.
    private static void AnalyzeMethodReference(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var reference = (IMethodReferenceOperation)context.Operation;
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            ReportForbiddenInInitializer(context, reference.Method.ContainingType);
            RecordInitializerRootCall(context, helperGraph, reference.Method);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, reference.Method.ContainingType);
        helperGraph.RecordCall(method, reference.Method, context.Operation.Syntax.GetLocation());
        ReportLocalUseIfInvalid(context, reference.Method);
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName)
        => symbol.GetAttributes().Any(a => string.Equals(
            a.AttributeClass?.ToDisplayString(),
            metadataName,
            StringComparison.Ordinal));

    private static void ReportAndRecordIfForbidden(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method,
        ITypeSymbol? type)
    {
        if (!IsForbiddenHostApi(type))
        {
            return;
        }

        helperGraph.RecordForbidden(method, type!);
        if (!IsEventKernel(method.ContainingType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            context.Operation.Syntax.GetLocation(),
            type!.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }

    private static bool IsForbiddenHostApi(ITypeSymbol? type)
    {
        var name = type?.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return IsForbiddenExactType(name!) || IsForbiddenNamespace(name!);
    }

    private static bool IsForbiddenExactType(string typeName)
        => typeName is DotBoxDGenerationNames.TypeNames.SystemActivator
            or DotBoxDGenerationNames.TypeNames.SystemEnvironment
            or DotBoxDGenerationNames.TypeNames.SystemGc
            or DotBoxDGenerationNames.TypeNames.SystemDelegate
            or DotBoxDGenerationNames.TypeNames.SystemServiceProvider
            or DotBoxDGenerationNames.TypeNames.SystemType;

    private static bool IsForbiddenNamespace(string typeName)
    {
        ReadOnlySpan<string> prefixes = [
            "System.IO.",
            "System.Net.",
            "System.Reflection.",
            "System.Runtime.InteropServices.",
            "System.Runtime.Loader.",
            "System.Diagnostics.",
            "System.Threading.",
            "System.Threading.Tasks.",
            "System.Linq.Expressions.",
            "System.Data.",
            "Microsoft.CSharp.",
            "Microsoft.EntityFrameworkCore."
        ];
        foreach (var prefix in prefixes)
        {
            if (typeName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsEventKernel(INamedTypeSymbol? type)
        => type?.AllInterfaces.Any(i => string.Equals(
            i.OriginalDefinition.ToDisplayString(),
            DotBoxDMetadataNames.EventKernelInterface,
            StringComparison.Ordinal)) == true;

    private static bool IsAllowedLiveSettingType(ITypeSymbol type)
        => DotBoxDTypeNameReader.IsSupportedLiveSettingType(type);

}
