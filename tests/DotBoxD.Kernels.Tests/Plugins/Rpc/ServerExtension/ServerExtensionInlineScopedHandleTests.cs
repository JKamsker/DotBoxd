using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

/// <summary>
/// Regression guard for issue #67: a <c>[ServerExtensionMethod]</c> that obtains a scoped host handle
/// <b>inline</b> — <c>_world.Monsters.Get(id).KillAsync()</c> — must lower identically to the
/// local-variable form (<c>var h = _world.Monsters.Get(id); h.KillAsync()</c>). Both capture the key passed
/// to <c>Get(id)</c> and thread it as the leading host-call argument. Before the fix the inline receiver
/// dropped the captured scope, so the package built clean but emitted a host call with no scope argument and
/// failed at install with <c>E-CALL-ARITY</c>; the local form (the only shape exercised by <c>samples/</c>)
/// lowered correctly. Both kernels below use a block body — RPC entrypoints require one — so the only
/// variable under test is the inline vs. local receiver.
/// </summary>
public sealed class ServerExtensionInlineScopedHandleTests
{
    private const string InlineKernel = """
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IGameWorldAccess
        {
            IMonsterControl Monsters { get; }
        }

        [RpcService]
        public interface IMonsterControl
        {
            [HostBinding("game.world.monster.read.handle", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            IMonster Get(string entityId);
        }

        [RpcService]
        public interface IMonster
        {
            [HostBinding("game.world.monster.write.kill", SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
            ValueTask<bool> KillAsync();
        }

        [ServerExtension(typeof(IMonsterControl))]
        public sealed partial class ScopedCallKernel
        {
            private readonly IGameWorldAccess _world;
            public ScopedCallKernel(IGameWorldAccess world) => _world = world;

            [ServerExtensionMethod]
            public async ValueTask<bool> KillOneAsync(string id, HookContext ctx)
            {
                return await _world.Monsters.Get(id).KillAsync();   // inline scoped receiver
            }
        }
        """;

    private const string LocalKernel = """
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IGameWorldAccess
        {
            IMonsterControl Monsters { get; }
        }

        [RpcService]
        public interface IMonsterControl
        {
            [HostBinding("game.world.monster.read.handle", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            IMonster Get(string entityId);
        }

        [RpcService]
        public interface IMonster
        {
            [HostBinding("game.world.monster.write.kill", SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
            ValueTask<bool> KillAsync();
        }

        [ServerExtension(typeof(IMonsterControl))]
        public sealed partial class ScopedCallKernel
        {
            private readonly IGameWorldAccess _world;
            public ScopedCallKernel(IGameWorldAccess world) => _world = world;

            [ServerExtensionMethod]
            public async ValueTask<bool> KillOneAsync(string id, HookContext ctx)
            {
                var monster = _world.Monsters.Get(id);              // scoped handle bound to a local first
                return await monster.KillAsync();
            }
        }
        """;

    [Fact]
    public void Inline_scoped_handle_call_keeps_the_captured_scope_in_the_lowered_host_call()
    {
        var localCall = HostCall(BuildPackage(LocalKernel), "KillAsync");
        var inlineCall = HostCall(BuildPackage(InlineKernel), "KillAsync");

        // Control: the local-variable form has always threaded the captured id as the only host-call argument.
        var localArgument = Assert.IsType<VariableExpression>(Assert.Single(localCall.Arguments));
        Assert.Equal("id", localArgument.Name);

        // The fix: the inline form lowers identically — the captured scope is not dropped.
        var inlineArgument = Assert.IsType<VariableExpression>(Assert.Single(inlineCall.Arguments));
        Assert.Equal("id", inlineArgument.Name);
    }

    [Fact]
    public void Parenthesized_inline_scoped_handle_call_keeps_the_captured_scope()
    {
        // A natural inline variant: the scoped receiver wrapped in parentheses must strip to the same lowering.
        var source = InlineKernel.Replace(
            "_world.Monsters.Get(id).KillAsync()",
            "(_world.Monsters.Get(id)).KillAsync()",
            StringComparison.Ordinal);
        Assert.Contains("(_world.Monsters.Get(id)).KillAsync()", source, StringComparison.Ordinal);

        var call = HostCall(BuildPackage(source), "KillAsync");

        var argument = Assert.IsType<VariableExpression>(Assert.Single(call.Arguments));
        Assert.Equal("id", argument.Name);
    }

    [Fact]
    public void Scoped_handle_alias_keeps_the_captured_scope()
    {
        var source = LocalKernel.Replace(
            "return await monster.KillAsync();",
            """
            var alias = monster;
                            return await alias.KillAsync();
            """,
            StringComparison.Ordinal);

        var call = HostCall(BuildPackage(source), "KillAsync");

        var argument = Assert.IsType<VariableExpression>(Assert.Single(call.Arguments));
        Assert.Equal("id", argument.Name);
    }

    [Fact]
    public void Scoped_handle_accessor_with_multiple_arguments_reports_DBXK100()
    {
        var source = InlineKernel
            .Replace("IMonster Get(string entityId);", "IMonster Get(string entityId, string shardId);", StringComparison.Ordinal)
            .Replace("_world.Monsters.Get(id).KillAsync()", "_world.Monsters.Get(id, id).KillAsync()", StringComparison.Ordinal);

        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("exactly one scope argument", StringComparison.Ordinal));
    }

    [Fact]
    public void Scoped_handle_accessor_with_ref_scope_argument_reports_DBXK100()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Services.Attributes;

            namespace Sample;

            [RpcService]
            public interface IGameWorldAccess
            {
                IMonsterControl Monsters { get; }
            }

            [RpcService]
            public interface IMonsterControl
            {
                [HostBinding("game.world.monster.read.handle", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                IMonster Get(ref string entityId);
            }

            [RpcService]
            public interface IMonster
            {
                [HostBinding("game.world.monster.read.threat", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int Threat();
            }

            [ServerExtension(typeof(IMonsterControl))]
            public sealed partial class ScopedCallKernel
            {
                private readonly IGameWorldAccess _world;
                public ScopedCallKernel(IGameWorldAccess world) => _world = world;

                public int Read(string id, HookContext ctx) => _world.Monsters.Get(ref id).Threat();
            }
            """);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("ref, in, or out", StringComparison.Ordinal));
    }

    [Fact]
    public void Inline_and_local_scoped_handle_kernels_require_the_same_capability()
    {
        var local = BuildPackage(LocalKernel);
        var inline = BuildPackage(InlineKernel);

        // KillAsync's capability is required; the Get(...) handle capability is intentionally NOT collected —
        // Get is a cheap local scope-capture, not a gated host call (see the GameServer sample's
        // MonsterKillerKernel manifest, which likewise omits game.world.monster.read.handle). The two receiver
        // forms must declare identical capabilities.
        Assert.Contains("game.world.monster.write.kill", local.Manifest.RequiredCapabilities);
        Assert.DoesNotContain("game.world.monster.read.handle", local.Manifest.RequiredCapabilities);
        Assert.Equal(
            local.Manifest.RequiredCapabilities,
            inline.Manifest.RequiredCapabilities);
    }

    private static PluginPackage BuildPackage(string source)
        => PluginAnalyzerGeneratedPackageFactory.Create(
            source,
            "Sample.ScopedCallPluginPackage",
            typeof(DotBoxD.Services.Attributes.RpcServiceAttribute));

    private static CallExpression HostCall(PluginPackage package, string methodSuffix)
        => package.Module.Functions
            .SelectMany(function => DescendantExpressions(function.Body))
            .OfType<CallExpression>()
            .Single(call => call.Name.StartsWith("host.", StringComparison.Ordinal)
                         && call.Name.EndsWith("." + methodSuffix, StringComparison.Ordinal));

    private static IEnumerable<Expression> DescendantExpressions(IEnumerable<Statement> statements)
        => statements.SelectMany(DescendantExpressions);

    // Walks every Statement/Expression node in the kernel IR. Kept in sync with the node kinds in
    // DotBoxD.Kernels.ModuleModel — a new statement/expression kind must be added here or HostCall could miss
    // a host call and the .Single() assertion would silently under-count.
    private static IEnumerable<Expression> DescendantExpressions(Statement statement)
        => statement switch
        {
            AssignmentStatement assignment => DescendantExpressions(assignment.Value),
            ReturnStatement @return => DescendantExpressions(@return.Value),
            ExpressionStatement expression => DescendantExpressions(expression.Value),
            IfStatement branch => DescendantExpressions(branch.Condition)
                .Concat(DescendantExpressions(branch.Then))
                .Concat(DescendantExpressions(branch.Else)),
            WhileStatement loop => DescendantExpressions(loop.Condition)
                .Concat(DescendantExpressions(loop.Body)),
            ForRangeStatement loop => DescendantExpressions(loop.Start)
                .Concat(DescendantExpressions(loop.End))
                .Concat(DescendantExpressions(loop.Body)),
            _ => []
        };

    private static IEnumerable<Expression> DescendantExpressions(Expression expression)
    {
        yield return expression;
        switch (expression)
        {
            case UnaryExpression unary:
                foreach (var inner in DescendantExpressions(unary.Operand))
                {
                    yield return inner;
                }

                break;
            case BinaryExpression binary:
                foreach (var inner in DescendantExpressions(binary.Left).Concat(DescendantExpressions(binary.Right)))
                {
                    yield return inner;
                }

                break;
            case CallExpression call:
                foreach (var inner in call.Arguments.SelectMany(DescendantExpressions))
                {
                    yield return inner;
                }

                break;
        }
    }
}
