using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

public sealed class PluginAnalyzerHostBindingArgumentTests
{
    private const string NamedDefaultHostBindingSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public interface IProbeWorld
        {
            [HostBinding("host.probe.canFight", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            bool CanFight(int monsterId, int threshold = 10, string mode = "raid");
        }

        public sealed record FightEvent(string TargetId, int MonsterId, string Message);

        [Plugin("named-default-host-binding")]
        public sealed partial class NamedDefaultHostBindingKernel : IEventKernel<FightEvent>
        {
            public bool ShouldHandle(FightEvent e, HookContext ctx)
                => ctx.Host<IProbeWorld>().CanFight(monsterId: e.MonsterId, mode: "raid");

            public void Handle(FightEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, e.Message);
        }
        """;

    private const string ReorderedNamedHostBindingSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public interface IProbeWorld
        {
            [HostBinding("host.probe.canFight", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            bool CanFight(int monsterId, int threshold);
        }

        public sealed record FightEvent(string TargetId, int MonsterId, int Threshold, string Message);

        [Plugin("reordered-named-host-binding")]
        public sealed partial class ReorderedNamedHostBindingKernel : IEventKernel<FightEvent>
        {
            public bool ShouldHandle(FightEvent e, HookContext ctx)
                => ctx.Host<IProbeWorld>().CanFight(threshold: e.Threshold, monsterId: e.MonsterId);

            public void Handle(FightEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, e.Message);
        }
        """;

    [Fact]
    public void Host_binding_named_arguments_and_defaults_lower_in_parameter_order()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            NamedDefaultHostBindingSource,
            "Sample.NamedDefaultHostBindingPluginPackage");

        var shouldHandle = package.Module.Functions.Single(function => function.Id == "ShouldHandle");
        var call = Assert.Single(Calls(shouldHandle.Body, "host.probe.canFight"));

        Assert.Collection(
            call.Arguments,
            argument => Assert.Equal("e_MonsterId", Assert.IsType<VariableExpression>(argument).Name),
            argument => Assert.Equal(10, Assert.IsType<I32Value>(Assert.IsType<LiteralExpression>(argument).Value).Value),
            argument => Assert.Equal("raid", Assert.IsType<StringValue>(Assert.IsType<LiteralExpression>(argument).Value).Value));
    }

    [Fact]
    public void Host_binding_reordered_named_arguments_report_generation_diagnostic()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(ReorderedNamedHostBindingSource);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                diagnostic.GetMessage().Contains("parameter order", StringComparison.Ordinal));
    }

    private static IEnumerable<CallExpression> Calls(IReadOnlyList<Statement> statements, string name)
    {
        foreach (var statement in statements)
        {
            foreach (var call in Calls(statement, name))
            {
                yield return call;
            }
        }
    }

    private static IEnumerable<CallExpression> Calls(Statement statement, string name)
    {
        switch (statement)
        {
            case ReturnStatement { Value: { } value }:
                return Calls(value, name);
            case IfStatement branch:
                return Calls(branch.Condition, name)
                    .Concat(Calls(branch.Then, name))
                    .Concat(Calls(branch.Else, name));
            default:
                return [];
        }
    }

    private static IEnumerable<CallExpression> Calls(Expression expression, string name)
    {
        if (expression is CallExpression call)
        {
            foreach (var argumentCall in call.Arguments.SelectMany(argument => Calls(argument, name)))
            {
                yield return argumentCall;
            }

            if (string.Equals(call.Name, name, StringComparison.Ordinal))
            {
                yield return call;
            }
        }
        else if (expression is BinaryExpression binary)
        {
            foreach (var nestedCall in Calls(binary.Left, name).Concat(Calls(binary.Right, name)))
            {
                yield return nestedCall;
            }
        }
        else if (expression is UnaryExpression unary)
        {
            foreach (var nestedCall in Calls(unary.Operand, name))
            {
                yield return nestedCall;
            }
        }
    }
}
