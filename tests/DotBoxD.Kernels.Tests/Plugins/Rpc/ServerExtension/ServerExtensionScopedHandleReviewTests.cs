using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionScopedHandleReviewTests
{
    private const string Prelude = """
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
        """;

    [Fact]
    public void Uninitialized_scoped_handle_local_reports_diagnostic()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(Prelude + """
            public ScopedCallKernel(IGameWorldAccess world) => _world = world;

            [ServerExtensionMethod]
            public async ValueTask<bool> KillOneAsync(string id, HookContext ctx)
            {
                IMonster monster;
                monster = _world.Monsters.Get(id);
                return await monster.KillAsync();
            }
        }
        """);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("must be initialized at declaration", StringComparison.Ordinal));
    }

    [Fact]
    public void This_member_receiver_does_not_reuse_same_named_scoped_handle_local()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            Prelude + """
                private readonly IMonster monster;

                public ScopedCallKernel(IGameWorldAccess world)
                {
                    _world = world;
                    monster = world.Monsters.Get("field");
                }

                [ServerExtensionMethod]
                public async ValueTask<bool> KillOneAsync(string id, HookContext ctx)
                {
                    var monster = _world.Monsters.Get(id);
                    return await this.monster.KillAsync();
                }
            }
            """,
            "Sample.ScopedCallPluginPackage",
            typeof(DotBoxD.Services.Attributes.RpcServiceAttribute));

        Assert.Empty(HostCall(package).Arguments);
    }

    private static CallExpression HostCall(PluginPackage package)
        => package.Module.Functions
            .SelectMany(function => DescendantExpressions(function.Body))
            .OfType<CallExpression>()
            .Single(call => call.Name.EndsWith(".KillAsync", StringComparison.Ordinal));

    private static IEnumerable<Expression> DescendantExpressions(IEnumerable<Statement> statements)
        => statements.SelectMany(DescendantExpressions);

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
