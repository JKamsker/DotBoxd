using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal sealed class ForbiddenHelperCallGraph
{
    private readonly ConcurrentDictionary<ISymbol, ITypeSymbol> _forbidden = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentBag<HelperEdge> _helperEdges = [];
    private readonly ConcurrentBag<RootHelperCall> _rootCalls = [];

    public void RecordForbidden(IMethodSymbol method, ITypeSymbol type)
        => _forbidden.TryAdd(method.OriginalDefinition, type);

    public void RecordCall(IMethodSymbol caller, IMethodSymbol target, Location location)
    {
        if (target.DeclaringSyntaxReferences.Length == 0 ||
            PluginAnalyzer.IsEventKernel(target.ContainingType))
        {
            return;
        }

        var normalizedTarget = target.OriginalDefinition;
        if (PluginAnalyzer.IsEventKernel(caller.ContainingType))
        {
            _rootCalls.Add(new RootHelperCall(normalizedTarget, location));
            return;
        }

        if (caller.DeclaringSyntaxReferences.Length == 0)
        {
            return;
        }

        _helperEdges.Add(new HelperEdge(caller.OriginalDefinition, normalizedTarget));
    }

    public void ReportDiagnostics(CompilationAnalysisContext context)
    {
        if (_forbidden.IsEmpty ||
            _rootCalls.IsEmpty)
        {
            return;
        }

        var tainted = PropagateForbiddenHelpers();
        foreach (var call in _rootCalls)
        {
            if (tainted.TryGetValue(call.Target, out var type))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PluginAnalyzer.ForbiddenHostApiRule,
                    call.Location,
                    type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            }
        }
    }

    private Dictionary<ISymbol, ITypeSymbol> PropagateForbiddenHelpers()
    {
        var tainted = new Dictionary<ISymbol, ITypeSymbol>(_forbidden, SymbolEqualityComparer.Default);
        var callersByTarget = new Dictionary<ISymbol, List<ISymbol>>(SymbolEqualityComparer.Default);
        foreach (var edge in _helperEdges)
        {
            if (!callersByTarget.TryGetValue(edge.Target, out var callers))
            {
                callers = [];
                callersByTarget.Add(edge.Target, callers);
            }

            callers.Add(edge.Caller);
        }

        var pending = new Queue<ISymbol>(tainted.Keys);
        while (pending.Count > 0)
        {
            var target = pending.Dequeue();
            if (!tainted.TryGetValue(target, out var type) ||
                !callersByTarget.TryGetValue(target, out var callers))
            {
                continue;
            }

            foreach (var caller in callers)
            {
                if (tainted.ContainsKey(caller))
                {
                    continue;
                }

                tainted.Add(caller, type);
                pending.Enqueue(caller);
            }
        }

        return tainted;
    }

    private readonly record struct HelperEdge(ISymbol Caller, ISymbol Target);

    private readonly record struct RootHelperCall(ISymbol Target, Location Location);
}
