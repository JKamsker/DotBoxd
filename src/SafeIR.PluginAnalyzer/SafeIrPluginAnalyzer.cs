namespace SafeIR.PluginAnalyzer;

using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SafeIrPluginAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor ForbiddenHostApiRule = new(
        "SGP001",
        "Forbidden host API is not allowed in plugin kernels",
        "Forbidden host API '{0}' is not allowed in this plugin contract",
        "SafeIR.Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Hook filters and kernel handlers must use approved safe facades instead of host APIs.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    public static readonly DiagnosticDescriptor LiveSettingTypeRule = new(
        "SGP020",
        "Live setting type is not supported",
        "Live setting type '{0}' is not supported",
        "SafeIR.Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Live settings must use supported scalar types.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(ForbiddenHostApiRule, LiveSettingTypeRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
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

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        if (!HasAttribute(property, SafeIrGenerationNames.Metadata.LiveSettingAttribute)) {
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
        => typeName is "System.Activator" or "System.Environment" or "System.GC"
            or "System.Delegate" or "System.IServiceProvider" or "System.Type";

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
            SafeIrGenerationNames.Metadata.EventKernelInterface,
            StringComparison.Ordinal)) == true;

    private static bool IsAllowedLiveSettingType(ITypeSymbol type)
    {
        return type.SpecialType is SpecialType.System_Boolean
            or SpecialType.System_Int32
            or SpecialType.System_Int64
            or SpecialType.System_Double
            or SpecialType.System_String;
    }

    private sealed class ForbiddenHelperCallGraph
    {
        private readonly ConcurrentDictionary<ISymbol, ITypeSymbol> _forbidden = new(SymbolEqualityComparer.Default);
        private readonly ConcurrentBag<HelperCall> _calls = [];

        public void RecordForbidden(IMethodSymbol method, ITypeSymbol type)
            => _forbidden.TryAdd(method.OriginalDefinition, type);

        public void RecordCall(IMethodSymbol caller, IMethodSymbol target, Location location)
        {
            if (target.DeclaringSyntaxReferences.Length == 0 ||
                IsEventKernel(target.ContainingType)) {
                return;
            }

            _calls.Add(new HelperCall(caller.OriginalDefinition, target.OriginalDefinition, location));
        }

        public void ReportDiagnostics(CompilationAnalysisContext context)
        {
            var tainted = PropagateForbiddenHelpers();
            foreach (var call in _calls) {
                if (IsEventKernel(call.Caller.ContainingType) &&
                    tainted.TryGetValue(call.Target, out var type)) {
                    context.ReportDiagnostic(Diagnostic.Create(
                        ForbiddenHostApiRule,
                        call.Location,
                        type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                }
            }
        }

        private Dictionary<ISymbol, ITypeSymbol> PropagateForbiddenHelpers()
        {
            var tainted = new Dictionary<ISymbol, ITypeSymbol>(_forbidden, SymbolEqualityComparer.Default);
            var changed = true;
            while (changed) {
                changed = false;
                foreach (var call in _calls) {
                    if (!tainted.ContainsKey(call.Caller) &&
                        tainted.TryGetValue(call.Target, out var type)) {
                        tainted[call.Caller] = type;
                        changed = true;
                    }
                }
            }

            return tainted;
        }
    }

    private sealed record HelperCall(IMethodSymbol Caller, IMethodSymbol Target, Location Location);
}
