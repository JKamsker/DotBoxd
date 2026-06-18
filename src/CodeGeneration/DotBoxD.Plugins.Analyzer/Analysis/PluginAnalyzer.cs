using System.Collections.Immutable;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PluginAnalyzer : DiagnosticAnalyzer
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

    // Phase C-0 (detection only): flag an inline Run(lambda) hook chain. Lowering these
    // lambdas to verified DotBoxD.Kernels is a later analyzer phase; until then the runtime terminal throws,
    // so this informational diagnostic warns the author at compile time.
    public static readonly DiagnosticDescriptor RunNotLoweredRule = new(
        "DBXK110",
        "Run chain is not yet lowered to verified IR",
        "Run(lambda) is not yet lowered to verified IR and will throw at runtime; bind a kernel class with Use/Register, or use RunLocal for native host code",
        "DotBoxD.Kernels.Generation",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Detection only: lowering inline Where/Select/Run chains to verified DotBoxD.Kernels is a future analyzer phase.",
        helpLinkUri: PluginAnalyzerDiagnostics.UnshippedRulesHelpLinkBase + "DBXK110");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(ForbiddenHostApiRule, LiveSettingTypeRule, RunNotLoweredRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
        context.RegisterOperationAction(AnalyzeHookChainTerminal, OperationKind.Invocation);
        context.RegisterCompilationStartAction(startContext =>
        {
            var helperGraph = new ForbiddenHelperCallGraph();
            startContext.RegisterOperationAction(c => AnalyzeInvocation(c, helperGraph), OperationKind.Invocation);
            startContext.RegisterOperationAction(c => AnalyzeObjectCreation(c, helperGraph), OperationKind.ObjectCreation);
            startContext.RegisterOperationAction(c => AnalyzePropertyReference(c, helperGraph), OperationKind.PropertyReference);
            startContext.RegisterOperationAction(c => AnalyzeFieldReference(c, helperGraph), OperationKind.FieldReference);
            startContext.RegisterOperationAction(c => AnalyzeTypeOf(c, helperGraph), OperationKind.TypeOf);
            startContext.RegisterCompilationEndAction(helperGraph.ReportDiagnostics);
        });
    }

    private static void AnalyzeHookChainTerminal(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!string.Equals(invocation.TargetMethod.Name, "Run", StringComparison.Ordinal))
        {
            return;
        }

        var containing = invocation.TargetMethod.ContainingType;
        if (containing is null ||
            !IsHookChainType(containing))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(RunNotLoweredRule, invocation.Syntax.GetLocation()));
    }

    private static bool IsHookChainType(INamedTypeSymbol type)
    {
        var original = type.OriginalDefinition.ToDisplayString();
        return string.Equals(original, DotBoxDGenerationNames.TypeNames.HookPipelineOriginal, StringComparison.Ordinal) ||
               string.Equals(original, DotBoxDGenerationNames.TypeNames.HookStageOriginal, StringComparison.Ordinal) ||
               string.Equals(original, DotBoxDGenerationNames.TypeNames.RemoteHookPipelineOriginal, StringComparison.Ordinal) ||
               string.Equals(original, DotBoxDGenerationNames.TypeNames.RemoteHookStageOriginal, StringComparison.Ordinal) ||
               string.Equals(original, DotBoxDGenerationNames.TypeNames.SubscriptionPipelineOriginal, StringComparison.Ordinal) ||
               string.Equals(original, DotBoxDGenerationNames.TypeNames.SubscriptionStageOriginal, StringComparison.Ordinal) ||
               string.Equals(original, DotBoxDGenerationNames.TypeNames.RemoteSubscriptionPipelineOriginal, StringComparison.Ordinal) ||
               string.Equals(original, DotBoxDGenerationNames.TypeNames.RemoteSubscriptionStageOriginal, StringComparison.Ordinal);
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        if (!HasAttribute(property, DotBoxDGenerationNames.Metadata.LiveSettingAttribute)) {
            return;
        }

        if (!IsAllowedLiveSettingType(property.Type)) {
            context.ReportDiagnostic(Diagnostic.Create(
                LiveSettingTypeRule,
                property.Locations.FirstOrDefault(),
                property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (context.ContainingSymbol is not IMethodSymbol method) {
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, invocation.TargetMethod.ContainingType);
        helperGraph.RecordCall(method, invocation.TargetMethod, context.Operation.Syntax.GetLocation());
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        if (context.ContainingSymbol is not IMethodSymbol method) {
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, ((IObjectCreationOperation)context.Operation).Type);
    }

    private static void AnalyzePropertyReference(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        if (context.ContainingSymbol is not IMethodSymbol method) {
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, ((IPropertyReferenceOperation)context.Operation).Property.ContainingType);
    }

    private static void AnalyzeFieldReference(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        if (context.ContainingSymbol is not IMethodSymbol method) {
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, ((IFieldReferenceOperation)context.Operation).Field.ContainingType);
    }

    private static void AnalyzeTypeOf(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        if (context.ContainingSymbol is not IMethodSymbol method) {
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, ((ITypeOfOperation)context.Operation).Type);
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
        if (!IsForbiddenHostApi(type)) {
            return;
        }

        helperGraph.RecordForbidden(method, type!);
        if (!IsEventKernel(method.ContainingType)) {
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
        if (string.IsNullOrWhiteSpace(name)) {
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
        foreach (var prefix in prefixes) {
            if (typeName.StartsWith(prefix, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    internal static bool IsEventKernel(INamedTypeSymbol? type)
        => type?.AllInterfaces.Any(i => string.Equals(
            i.OriginalDefinition.ToDisplayString(),
            DotBoxDGenerationNames.Metadata.EventKernelInterface,
            StringComparison.Ordinal)) == true;

    private static bool IsAllowedLiveSettingType(ITypeSymbol type)
        => DotBoxDTypeNameReader.IsSupportedScalar(type);

}
