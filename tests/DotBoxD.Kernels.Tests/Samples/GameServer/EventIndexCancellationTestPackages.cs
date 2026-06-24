extern alias GameServerAbstractions;

using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using GameServerAbstractions::DotBoxD.Kernels.Game.Server.Abstractions.Events;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed partial class EventIndexCancellationTests
{
    private static readonly SourceSpan Span = new(1, 1);

    private static PluginPackage CancellationPackage(bool cancelInFilter)
    {
        const string pluginId = "indexed-cancellation";
        var manifest = new PluginManifest(
            pluginId,
            "IEventKernel<DotBoxD.Kernels.Game.Server.Abstractions.Events.AttackEvent>",
            ExecutionMode.Auto,
            ["Cpu", "Alloc"],
            [],
            [
                new HookSubscriptionManifest(
                    "DotBoxD.Kernels.Game.Server.Abstractions.Events.AttackEvent",
                    pluginId)
                {
                    IndexedPredicates =
                    [
                        new IndexedPredicate(
                            nameof(AttackEvent.AttackerId),
                            IndexPredicateOperator.Equals,
                            "player-1",
                            "string")
                    ]
                }
            ]);
        var module = new SandboxModule(
            pluginId,
            SemVersion.One,
            SemVersion.One,
            [],
            [CancellationShouldHandle(cancelInFilter), CancellationHandle(cancelInFilter)],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["kernel"] = pluginId,
                ["pluginId"] = pluginId
            });
        return PluginPackage.Create(manifest, module, new KernelEntrypoints("ShouldHandle", "Handle"));
    }

    private static SandboxFunction CancellationShouldHandle(bool cancelInFilter)
    {
        Expression predicate = new BinaryExpression(
            new VariableExpression("e_AttackerId", Span),
            "==",
            new LiteralExpression(SandboxValue.FromString("player-1"), Span),
            Span);
        if (cancelInFilter)
        {
            predicate = new BinaryExpression(
                new CallExpression("test.cancel", [], null, Span),
                "&&",
                predicate,
                Span);
        }

        return new SandboxFunction(
            "ShouldHandle",
            true,
            EventParameters(),
            SandboxType.Bool,
            [new ReturnStatement(predicate, Span)]);
    }

    private static SandboxFunction CancellationHandle(bool cancelInFilter)
    {
        var body = new List<Statement>();
        if (!cancelInFilter)
        {
            body.Add(new ExpressionStatement(new CallExpression("test.cancel", [], null, Span), Span));
        }

        body.Add(new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span));
        return new SandboxFunction("Handle", true, EventParameters(), SandboxType.Unit, body);
    }

    private static Parameter[] EventParameters()
        =>
        [
            new("e_AttackerId", SandboxType.String),
            new("e_TargetId", SandboxType.String),
            new("e_Damage", SandboxType.I32),
            new("e_AttackerLevel", SandboxType.I32)
        ];

    private static BindingDescriptor CancelBinding(Action cancel)
        => new(
            "test.cancel",
            SemVersion.One,
            [],
            SandboxType.Bool,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) =>
            {
                cancel();
                return ValueTask.FromResult(SandboxValue.FromBool(true));
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static async Task AssertNoFaultAsync(Task faultTask)
    {
        var delay = Task.Delay(TimeSpan.FromMilliseconds(150));
        var completed = await Task.WhenAny(faultTask, delay).ConfigureAwait(false);
        Assert.Same(delay, completed);
    }
}
