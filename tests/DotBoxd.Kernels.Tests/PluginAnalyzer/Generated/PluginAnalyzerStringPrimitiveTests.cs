using DotBoxd.Hosting;
using DotBoxd.Plugins;

namespace DotBoxd.Kernels.Tests;

public sealed class PluginAnalyzerStringPrimitiveTests
{
    private const int FuelLimit = 100_000;
    private const int HostCallLimit = 1_000;

    public static TheoryData<ExecutionMode> Modes()
        => new() {
            ExecutionMode.Interpreted,
            ExecutionMode.Compiled
        };

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Generated_should_handle_executes_string_length_substring_and_equals(
        ExecutionMode mode)
    {
        var package = CreatePackage();
        Assert.Contains("Alloc", package.Manifest.Effects);
        var host = SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(new InMemoryPluginMessageSink());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var policy = SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(FuelLimit)
            .WithWallTime(TimeSpan.FromSeconds(30))
            .WithMaxHostCalls(HostCallLimit)
            .Build();
        var plan = await host.PrepareAsync(package.Module, policy);

        await AssertShouldHandleAsync(host, plan, package, Input("fire bolt"), expected: true, mode);
        await AssertShouldHandleAsync(host, plan, package, Input("ice bolt"), expected: false, mode);
        await AssertShouldHandleAsync(host, plan, package, Input("fir"), expected: false, mode);
    }

    [Fact]
    public void Generated_should_handle_lowers_string_equality_to_string_binding()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxd.Plugins;
            using DotBoxd.Abstractions;

            namespace Sample;

            public sealed record StringEqualityEvent(string TargetId, string Message);

            [Plugin("generated-string-equality")]
            public sealed partial class StringEqualityKernel : IEventKernel<StringEqualityEvent>
            {
                public bool ShouldHandle(StringEqualityEvent e, HookContext ctx)
                    => e.Message == "fire" && e.TargetId != "";

                public void Handle(StringEqualityEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """, "Sample.StringEqualityPluginPackage");

        var shouldHandle = package.Module.Functions.Single(
            function => function.Id == "ShouldHandle");

        Assert.Equal(2, CountCalls(shouldHandle.Body, DotBoxdStringEqualsBinding));
    }

    private static PluginPackage CreatePackage()
        => PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxd.Plugins;
            using DotBoxd.Abstractions;

            namespace Sample;

            public sealed record StringPrimitiveEvent(string TargetId, string Message);

            [Plugin("generated-string-primitives")]
            public sealed partial class StringPrimitiveKernel : IEventKernel<StringPrimitiveEvent>
            {
                public bool ShouldHandle(StringPrimitiveEvent e, HookContext ctx)
                    => e.Message.Length >= 4 &&
                       e.Message.Substring(startIndex: 0, length: 4).Equals("fire");

                public void Handle(StringPrimitiveEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """, "Sample.StringPrimitivePluginPackage");

    private static async Task AssertShouldHandleAsync(
        SandboxHost host,
        ExecutionPlan plan,
        PluginPackage package,
        SandboxValue input,
        bool expected,
        ExecutionMode mode)
    {
        var result = await host.ExecuteAsync(
            plan,
            package.Entrypoints.ShouldHandle,
            input,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(expected, ((BoolValue)result.Value!).Value);
        Assert.Equal(mode, result.ActualMode);
    }

    private static SandboxValue Input(string message)
        => SandboxValue.FromList([
            SandboxValue.FromString("player-1"),
            SandboxValue.FromString(message)
        ]);

    private const string DotBoxdStringEqualsBinding = "string.equals";

    private static int CountCalls(IReadOnlyList<Statement> statements, string name)
    {
        var count = 0;
        foreach (var statement in statements)
        {
            count += CountCalls(statement, name);
        }

        return count;
    }

    private static int CountCalls(Statement statement, string name)
        => statement switch {
            AssignmentStatement assignment => CountCalls(assignment.Value, name),
            ReturnStatement ret => CountCalls(ret.Value, name),
            ExpressionStatement expression => CountCalls(expression.Value, name),
            IfStatement branch => CountCalls(branch.Then, name) +
                                  CountCalls(branch.Else, name) +
                                  CountCalls(branch.Condition, name),
            WhileStatement loop => CountCalls(loop.Body, name) + CountCalls(loop.Condition, name),
            ForRangeStatement range => CountCalls(range.Body, name) +
                                       CountCalls(range.Start, name) +
                                       CountCalls(range.End, name),
            _ => 0
        };

    private static int CountCalls(Expression expression, string name)
        => expression switch {
            UnaryExpression unary => CountCalls(unary.Operand, name),
            BinaryExpression binary => CountCalls(binary.Left, name) + CountCalls(binary.Right, name),
            CallExpression call => (call.Name == name ? 1 : 0) +
                                   call.Arguments.Sum(argument => CountCalls(argument, name)),
            _ => 0
        };
}
