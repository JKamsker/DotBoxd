using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters.Loops;

internal sealed partial class RawI32ExpressionPlan
{
    private static bool TryCreateMathIntrinsic(
        CallExpression call,
        LocalStackKindPlanner stackPlan,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        IBindingCatalog bindings,
        IReadOnlyDictionary<string, RawI32ExpressionPlan>? substitutions,
        out RawI32ExpressionPlan plan)
    {
        plan = null!;
        if (!TryGetMathKind(call.Name, out var kind) ||
            !CanUseDirectIntrinsic(bindings, call.Name, kind) ||
            !TryCreateArguments(call, kind, stackPlan, functions, bindings, substitutions, out var left, out var right, out var third))
        {
            return false;
        }

        plan = new RawI32ExpressionPlan(
            kind,
            name: call.Name,
            left: left,
            right: right,
            third: third);
        return true;
    }

    private static bool TryCreateArguments(
        CallExpression call,
        ExpressionKind kind,
        LocalStackKindPlanner stackPlan,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        IBindingCatalog bindings,
        IReadOnlyDictionary<string, RawI32ExpressionPlan>? substitutions,
        out RawI32ExpressionPlan? left,
        out RawI32ExpressionPlan? right,
        out RawI32ExpressionPlan? third)
    {
        left = right = third = null;
        var count = MathArgumentCount(kind);
        if (call.Arguments.Count != count ||
            !TryCreate(call.Arguments[0], stackPlan, functions, bindings, substitutions, out var first))
        {
            return false;
        }

        left = first;
        if (count == 1)
        {
            return true;
        }

        if (!TryCreate(call.Arguments[1], stackPlan, functions, bindings, substitutions, out var second))
        {
            return false;
        }

        right = second;
        if (count == 2)
        {
            return true;
        }

        if (!TryCreate(call.Arguments[2], stackPlan, functions, bindings, substitutions, out var thirdArg))
        {
            return false;
        }

        third = thirdArg;
        return true;
    }

    private static bool CanUseDirectIntrinsic(IBindingCatalog bindings, string id, ExpressionKind kind)
        => bindings.TryGet(id, out var binding) &&
           binding.Compiled is { Kind: "RuntimeStub" } &&
           binding.Compiled.Type == typeof(CompiledRuntime).FullName &&
           binding.Compiled.Method == BoxedMethod(kind) &&
           binding.Parameters.Count == MathArgumentCount(kind) &&
           ParametersAreI32(binding.Parameters) &&
           binding.ReturnType.Equals(SandboxType.I32) &&
           binding.RequiredCapability is null &&
           binding.Safety == BindingSafety.PureIntrinsic &&
           binding.AuditLevel == AuditLevel.None &&
           binding.CostModel.MaxCallsPerRun is null &&
           (binding.Effects & ~(SandboxEffect.Cpu | SandboxEffect.Alloc)) == SandboxEffect.None;

    private static bool ParametersAreI32(IReadOnlyList<SandboxType> parameters)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            if (!parameters[i].Equals(SandboxType.I32))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetMathKind(string id, out ExpressionKind kind)
    {
        kind = id switch {
            "math.abs" => ExpressionKind.Abs,
            "math.min" => ExpressionKind.Min,
            "math.max" => ExpressionKind.Max,
            "math.clamp" => ExpressionKind.Clamp,
            _ => ExpressionKind.Literal
        };
        return kind is not ExpressionKind.Literal;
    }

    private static int MathArgumentCount(ExpressionKind kind)
        => kind switch {
            ExpressionKind.Abs => 1,
            ExpressionKind.Min or ExpressionKind.Max => 2,
            ExpressionKind.Clamp => 3,
            _ => 0
        };

    private static string BoxedMethod(ExpressionKind kind)
        => kind switch {
            ExpressionKind.Abs => nameof(CompiledRuntime.AbsI32),
            ExpressionKind.Min => nameof(CompiledRuntime.MinI32),
            ExpressionKind.Max => nameof(CompiledRuntime.MaxI32),
            ExpressionKind.Clamp => nameof(CompiledRuntime.ClampI32),
            _ => ""
        };
}
