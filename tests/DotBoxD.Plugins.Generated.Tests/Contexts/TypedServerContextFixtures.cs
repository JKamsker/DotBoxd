using DotBoxD.Abstractions;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Generated.Tests.Contexts;

public sealed record TypedSignalEvent(string TargetId, int Distance);

[Hook("typed.damage", typeof(TypedDamageResult))]
public sealed record TypedDamageContext(string TargetId, int Distance, int Damage);

[HookResult]
public readonly partial record struct TypedDamageResult(bool Success, string? Reason, int Damage);

public sealed class TypedHookServerContext
{
    private readonly HookContext _inner;

    public TypedHookServerContext(HookContext inner) => _inner = inner;

    public static TypedHookServerContext Create(HookContext inner) => new(inner);

    public string NativeLabel => _inner.CancellationToken.CanBeCanceled ? "hook-native-cancelable" : "hook-native";

    public int NativeAdjustment => 11;

    [KernelMethod]
    public bool IsNear(int distance) => distance <= 4;

    [KernelMethod]
    public string Tag(string targetId) => targetId + ":hook";

    [HostBinding(
        "host.typed.hook.label",
        "typed.context.read.hookLabel",
        SandboxEffect.Cpu | SandboxEffect.HostStateRead | SandboxEffect.Audit)]
    public string Label
        => throw new NotSupportedException("Context host bindings are lowering markers.");

    [HostBinding(
        "host.typed.hook.adjustment",
        "typed.context.read.hookAdjustment",
        SandboxEffect.Cpu | SandboxEffect.HostStateRead | SandboxEffect.Audit)]
    public int Adjustment
        => throw new NotSupportedException("Context host bindings are lowering markers.");

    [HostBinding(
        "host.typed.hook.deliver",
        "typed.context.write.hookDeliver",
        SandboxEffect.Cpu | SandboxEffect.HostStateWrite | SandboxEffect.Audit)]
    public void Deliver(string targetId, string label)
        => throw new NotSupportedException("Context host bindings are lowering markers.");
}

public sealed class TypedSubscriptionServerContext
{
    private readonly HookContext _inner;

    public TypedSubscriptionServerContext(HookContext inner) => _inner = inner;

    public static TypedSubscriptionServerContext Create(HookContext inner) => new(inner);

    public string NativeLabel => _inner.CancellationToken.CanBeCanceled
        ? "subscription-native-cancelable"
        : "subscription-native";

    [KernelMethod]
    public bool ShouldReceive(int distance) => distance <= 3;

    [KernelMethod]
    public string Tag(string targetId) => targetId + ":subscription";

    [HostBinding(
        "host.typed.subscription.label",
        "typed.context.read.subscriptionLabel",
        SandboxEffect.Cpu | SandboxEffect.HostStateRead | SandboxEffect.Audit)]
    public string Label
        => throw new NotSupportedException("Context host bindings are lowering markers.");

    [HostBinding(
        "host.typed.subscription.deliver",
        "typed.context.write.subscriptionDeliver",
        SandboxEffect.Cpu | SandboxEffect.HostStateWrite | SandboxEffect.Audit)]
    public void Deliver(string targetId, string label)
        => throw new NotSupportedException("Context host bindings are lowering markers.");
}

internal sealed class TypedContextBindingState
{
    public List<string> HookDeliveries { get; } = [];

    public List<string> SubscriptionDeliveries { get; } = [];
}

internal static class TypedContextBindings
{
    public const string HookLabel = "hook-label";
    public const int HookAdjustment = 7;
    public const string SubscriptionLabel = "subscription-label";

    private const SandboxEffect ReadEffects = SandboxEffect.Cpu | SandboxEffect.HostStateRead | SandboxEffect.Audit;
    private const SandboxEffect WriteEffects = SandboxEffect.Cpu | SandboxEffect.HostStateWrite | SandboxEffect.Audit;

    public static SandboxPolicy Policy(bool read = false, bool write = false)
    {
        var builder = SandboxPolicyBuilder.Create();
        if (read)
        {
            builder.Grant("typed.context.read.*", new { }, SandboxEffect.HostStateRead | SandboxEffect.Audit);
        }

        if (write)
        {
            builder.Grant("typed.context.write.*", new { }, SandboxEffect.HostStateWrite | SandboxEffect.Audit);
        }

        return builder
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();
    }

    public static void AddBindings(SandboxHostBuilder builder, TypedContextBindingState state)
    {
        builder.AddBinding(StringBinding("host.typed.hook.label", "typed.context.read.hookLabel", HookLabel));
        builder.AddBinding(IntBinding("host.typed.hook.adjustment", "typed.context.read.hookAdjustment", HookAdjustment));
        builder.AddBinding(DeliveryBinding(
            "host.typed.hook.deliver",
            "typed.context.write.hookDeliver",
            state.HookDeliveries));
        builder.AddBinding(StringBinding(
            "host.typed.subscription.label",
            "typed.context.read.subscriptionLabel",
            SubscriptionLabel));
        builder.AddBinding(DeliveryBinding(
            "host.typed.subscription.deliver",
            "typed.context.write.subscriptionDeliver",
            state.SubscriptionDeliveries));
    }

    private static BindingDescriptor StringBinding(string id, string capability, string value)
        => new(
            id, SemVersion.One, [], SandboxType.String, ReadEffects, capability,
            BindingCostModel.Fixed(1), AuditLevel.PerCall, BindingSafety.ReadOnlyExternal,
            (context, _, _) =>
            {
                WriteAudit(context, id, capability, ReadEffects, "typed-context");
                return ValueTask.FromResult(SandboxValue.FromString(value));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { });

    private static BindingDescriptor IntBinding(string id, string capability, int value)
        => new(
            id, SemVersion.One, [], SandboxType.I32, ReadEffects, capability,
            BindingCostModel.Fixed(1), AuditLevel.PerCall, BindingSafety.ReadOnlyExternal,
            (context, _, _) =>
            {
                WriteAudit(context, id, capability, ReadEffects, "typed-context");
                return ValueTask.FromResult(SandboxValue.FromInt32(value));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { });

    private static BindingDescriptor DeliveryBinding(
        string id,
        string capability,
        List<string> deliveries)
        => new(
            id, SemVersion.One, [SandboxType.String, SandboxType.String], SandboxType.Unit, WriteEffects, capability,
            BindingCostModel.Fixed(1), AuditLevel.PerCall, BindingSafety.SideEffectingExternal,
            (context, args, _) =>
            {
                var targetId = ((StringValue)args[0]).Value;
                var label = ((StringValue)args[1]).Value;
                WriteAudit(context, id, capability, WriteEffects, "target:" + targetId);
                deliveries.Add(targetId + "|" + label);
                return ValueTask.FromResult(SandboxValue.Unit);
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { });

    private static void WriteAudit(
        SandboxContext context,
        string bindingId,
        string capability,
        SandboxEffect effects,
        string resourceId)
    {
        var startedAt = DateTimeOffset.UtcNow;
        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "BindingCall",
            startedAt,
            true,
            BindingId: bindingId,
            CapabilityId: capability,
            Effect: effects,
            ResourceId: resourceId,
            Fields: context.BindingAuditFields("typed-context", startedAt)));
    }
}
