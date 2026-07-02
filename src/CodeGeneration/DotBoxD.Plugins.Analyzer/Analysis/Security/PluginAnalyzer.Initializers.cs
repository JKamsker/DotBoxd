using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

// Reachability of forbidden host APIs through field/property initializers (the non-method-body path). An
// initializer's ContainingSymbol is the field or property symbol (not a method), so the method-body operation
// handlers skip it; these helpers report a directly-used forbidden type and seed the call graph with the
// initializer as a ROOT so transitively-reached forbidden APIs are still flagged at the initializer site.
public sealed partial class PluginAnalyzer
{
    // Field/property initializers run when the kernel type is constructed in-host, so a forbidden host API
    // used directly in one is as reachable as one in a method body. Their ContainingSymbol is the field or
    // property symbol (not a method), so the method-body handlers skip them; report the direct use here.
    private static void ReportForbiddenInInitializer(OperationAnalysisContext context, ITypeSymbol? type)
    {
        if (context.ContainingSymbol is not (IFieldSymbol or IPropertySymbol) ||
            !IsForbiddenHostApi(type) ||
            !IsEventKernel(context.ContainingSymbol.ContainingType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            context.Operation.Syntax.GetLocation(),
            type!.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }

    // A helper invoked (or referenced as a method group) from a field/property initializer in an event kernel is
    // a call-graph root: its body may transitively reach a forbidden host API even though the directly referenced
    // type is benign. Record it so the existing taint propagation flags it at the initializer site.
    private static void RecordInitializerRootCall(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol target)
    {
        if (context.ContainingSymbol is not (IFieldSymbol or IPropertySymbol))
        {
            return;
        }

        helperGraph.RecordInitializerRootCall(
            context.ContainingSymbol.ContainingType,
            target,
            context.Operation.Syntax.GetLocation());
    }

    // A helper property read from an initializer reaches a forbidden API through the accessor it actually uses, so
    // record a root for the getter on a read and the setter on a write (compound/increment uses both), mirroring
    // the method-body property path in AnalyzePropertyReference.
    private static void RecordInitializerPropertyRootCall(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IPropertySymbol property)
    {
        if (context.ContainingSymbol is not (IFieldSymbol or IPropertySymbol))
        {
            return;
        }

        var containingType = context.ContainingSymbol.ContainingType;
        var (usesGetter, usesSetter) = AccessorUsage(context.Operation);
        var location = context.Operation.Syntax.GetLocation();
        if (usesGetter && property.GetMethod is { } getter)
        {
            helperGraph.RecordInitializerRootCall(containingType, getter, location);
        }

        if (usesSetter && property.SetMethod is { } setter)
        {
            helperGraph.RecordInitializerRootCall(containingType, setter, location);
        }
    }

    private static (bool Getter, bool Setter) AccessorUsage(IOperation reference)
    {
        if (reference.Parent is IIncrementOrDecrementOperation increment && ReferenceEquals(increment.Target, reference))
        {
            return (true, true);
        }

        if (reference.Parent is IAssignmentOperation assignment && ReferenceEquals(assignment.Target, reference))
        {
            return assignment is ISimpleAssignmentOperation ? (false, true) : (true, true);
        }

        return (true, false);
    }
}
