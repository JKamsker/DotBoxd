using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Validation;

using System.Diagnostics.CodeAnalysis;
using DotBoxD.Kernels;

internal sealed class FunctionScope
{
    private Dictionary<string, SandboxType>? _locals;
    private readonly FunctionScope? _parent;

    private FunctionScope(Dictionary<string, SandboxType>? locals, FunctionScope? parent)
    {
        _locals = locals;
        _parent = parent;
    }

    public static FunctionScope FromParameters(IReadOnlyList<Parameter> parameters)
    {
        if (parameters.Count == 0)
        {
            return new FunctionScope(locals: null, parent: null);
        }

        var locals = new Dictionary<string, SandboxType>(parameters.Count, StringComparer.Ordinal);
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            locals.Add(parameter.Name, parameter.Type);
        }

        return new FunctionScope(locals, parent: null);
    }

    // Copy-on-write child scope: writes land in this overlay only and lookups fall
    // back to the parent chain, so a block stores just the locals it introduces or
    // changes instead of copying every visible local.
    public FunctionScope Clone() => new(locals: null, parent: this);

    public void MergeContinuingBranches(
        FunctionScope thenScope,
        bool thenReturns,
        FunctionScope elseScope,
        bool elseReturns,
        List<SandboxDiagnostic> diagnostics,
        SourceSpan span)
    {
        if (thenReturns && elseReturns)
        {
            return;
        }

        if (thenReturns)
        {
            MergeAll(elseScope, diagnostics, span);
            return;
        }

        if (elseReturns)
        {
            MergeAll(thenScope, diagnostics, span);
            return;
        }

        if (thenScope._locals is null || elseScope._locals is null)
        {
            return;
        }

        foreach (var (name, type) in thenScope._locals)
        {
            if (elseScope._locals.TryGetValue(name, out var elseType) && elseType == type)
            {
                Set(name, type, diagnostics, span);
            }
        }
    }

    public SandboxType Get(string name, List<SandboxDiagnostic> diagnostics, SourceSpan span)
    {
        if (TryResolve(name, out var type))
        {
            return type;
        }

        diagnostics.Add(new SandboxDiagnostic("E-LOCAL-UNKNOWN", $"unknown local '{name}'", Span: span));
        return SandboxType.Unit;
    }

    public void Set(string name, SandboxType type, List<SandboxDiagnostic> diagnostics, SourceSpan span)
    {
        if (TryResolve(name, out var existing) && existing != type)
        {
            diagnostics.Add(new SandboxDiagnostic("E-LOCAL-TYPE", $"local '{name}' changes type from {existing} to {type}", Span: span));
            return;
        }

        (_locals ??= new Dictionary<string, SandboxType>(StringComparer.Ordinal))[name] = type;
    }

    private void MergeAll(FunctionScope branchScope, List<SandboxDiagnostic> diagnostics, SourceSpan span)
    {
        if (branchScope._locals is null)
        {
            return;
        }

        foreach (var (name, type) in branchScope._locals)
        {
            Set(name, type, diagnostics, span);
        }
    }

    private bool TryResolve(string name, [MaybeNullWhen(false)] out SandboxType type)
    {
        for (var scope = this; scope is not null; scope = scope._parent)
        {
            if (scope._locals is not null && scope._locals.TryGetValue(name, out type))
            {
                return true;
            }
        }

        type = null;
        return false;
    }
}
