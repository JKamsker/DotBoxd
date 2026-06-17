using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters;

using DotBoxD.Kernels;

internal sealed class LocalStackKindPlanner
{
    private readonly SandboxFunction _function;
    private readonly IBindingCatalog _bindings;
    private readonly IReadOnlyDictionary<string, FunctionAnalysis> _functionAnalysis;
    private readonly Dictionary<string, StackKind> _localKinds = new(StringComparer.Ordinal);

    public LocalStackKindPlanner(
        SandboxFunction function,
        IBindingCatalog bindings,
        IReadOnlyDictionary<string, FunctionAnalysis> functionAnalysis)
    {
        _function = function;
        _bindings = bindings;
        _functionAnalysis = functionAnalysis;
        InferLocalKinds();
    }

    public StackKind LocalKind(string name)
        => _localKinds.TryGetValue(name, out var kind) ? kind : StackKind.Boxed;

    public SandboxType? Infer(Expression expression)
        => Infer(expression, _localKinds);

    private SandboxType? Infer(Expression expression, IReadOnlyDictionary<string, StackKind> localKinds)
        => expression switch
        {
            LiteralExpression literal => literal.Value.Type,
            VariableExpression variable => localKinds.TryGetValue(variable.Name, out var kind) && TypeOf(kind) is { } type
                ? type
                : LocalSeedType(variable.Name),
            UnaryExpression { Operator: "!" } => SandboxType.Bool,
            UnaryExpression unary => Infer(unary.Operand, localKinds),
            BinaryExpression binary => binary.Operator is "&&" or "||" or "==" or "!=" or "<" or "<=" or ">" or ">="
                ? SandboxType.Bool
                : Infer(binary.Left, localKinds),
            CallExpression call => InferCallType(call),
            _ => null
        };

    private void InferLocalKinds()
    {
        var candidates = new Dictionary<string, StackKind>(StringComparer.Ordinal);
        foreach (var parameter in _function.Parameters)
        {
            Observe(candidates, parameter.Name, KindOf(parameter.Type));
        }

        ScanLocalKinds(_function.Body, candidates);
        foreach (var pair in candidates)
        {
            if (pair.Value != StackKind.Boxed)
            {
                _localKinds[pair.Key] = pair.Value;
            }
        }
    }

    private void ScanLocalKinds(IReadOnlyList<Statement> statements, Dictionary<string, StackKind> candidates)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case AssignmentStatement assignment:
                    Observe(candidates, assignment.Name, KindOf(Infer(assignment.Value, candidates)));
                    break;
                case IfStatement branch:
                    ScanLocalKinds(branch.Then, candidates);
                    ScanLocalKinds(branch.Else, candidates);
                    break;
                case ForRangeStatement range:
                    Observe(candidates, range.LocalName, StackKind.I32);
                    ScanLocalKinds(range.Body, candidates);
                    break;
                case WhileStatement loop:
                    ScanLocalKinds(loop.Body, candidates);
                    break;
            }
        }
    }

    private static void Observe(Dictionary<string, StackKind> candidates, string name, StackKind kind)
    {
        if (!candidates.TryGetValue(name, out var existing))
        {
            candidates[name] = kind;
            return;
        }

        candidates[name] = existing == kind ? existing : StackKind.Boxed;
    }

    private static StackKind KindOf(SandboxType? type)
        => type switch {
            { Name: "I32" } => StackKind.I32,
            { Name: "I64" } => StackKind.I64,
            { Name: "F64" } => StackKind.F64,
            _ => StackKind.Boxed
        };

    private static SandboxType? TypeOf(StackKind kind)
        => kind switch {
            StackKind.I32 => SandboxType.I32,
            StackKind.I64 => SandboxType.I64,
            StackKind.F64 => SandboxType.F64,
            _ => null
        };

    private SandboxType? LocalSeedType(string name)
    {
        foreach (var parameter in _function.Parameters)
        {
            if (string.Equals(parameter.Name, name, StringComparison.Ordinal))
            {
                return parameter.Type;
            }
        }

        return null;
    }

    private SandboxType? InferCallType(CallExpression call)
        => call.Name switch {
            "list.count" => SandboxType.I32,
            "numeric.toI64" => SandboxType.I64,
            "numeric.toF64" => SandboxType.F64,
            _ => _functionAnalysis.TryGetValue(call.Name, out var analysis)
                ? analysis.ReturnType
                : _bindings.TryGet(call.Name, out var binding) ? binding.ReturnType : null
        };
}
