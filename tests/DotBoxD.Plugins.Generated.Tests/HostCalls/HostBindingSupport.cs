using DotBoxD.Abstractions;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Policies;

namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>A host world read by a scalar projection: <c>GetValue</c> returns a fixed int for any id.</summary>
public interface IScalarWorld
{
    [HostBinding("host.probe.getValue", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    int GetValue(string id);
}

/// <summary>A host world returning a list whose length depends on the id (3 tags for "crypt", otherwise 1). A list
/// return is classified as allocating, so the binding carries the Alloc effect.</summary>
public interface ITagWorld
{
    [HostBinding(
        "host.probe.getTags",
        "probe.read.tags",
        SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead)]
    IReadOnlyList<string> GetTags(string id);
}

/// <summary>A host world returning a Guid — an allocating host call, so the binding carries the Alloc effect.</summary>
public interface IIdWorld
{
    [HostBinding(
        "host.probe.generateId",
        "probe.read.id",
        SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead)]
    Guid GenerateId(string zone);
}

/// <summary>
/// Recreates the probe host bindings the <c>ctx.Host&lt;T&gt;()</c> projection tests call, plus the policy that grants
/// their capability. The interfaces above carry <c>[HostBinding]</c> so the analyzer routes each call to the matching
/// descriptor; the descriptors below run host-side and return the probe values.
/// </summary>
internal static class HostBindingSupport
{
    public const int ScalarValue = 42;

    public static readonly string[] CryptTags = ["alpha", "beta", "gamma"];

    private const SandboxEffect ScalarReadEffects = SandboxEffect.Cpu | SandboxEffect.HostStateRead;
    private const SandboxEffect AllocatingReadEffects =
        SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead;

    public static SandboxPolicy ProbeReadPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .Grant("probe.read.*", new { }, SandboxEffect.HostStateRead)
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    public static void AddBindings(SandboxHostBuilder builder)
    {
        builder.AddBinding(ScalarBinding());
        builder.AddBinding(TagsBinding());
        builder.AddBinding(IdBinding());
    }

    private static BindingDescriptor ScalarBinding()
        => new(
            "host.probe.getValue", SemVersion.One, [SandboxType.String], SandboxType.I32,
            ScalarReadEffects, "probe.read.value",
            BindingCostModel.Fixed(2), AuditLevel.PerResource, BindingSafety.ReadOnlyExternal,
            (context, args, _) =>
            {
                WriteAudit(context, "host.probe.getValue", "probe.read.value", ScalarReadEffects, args);
                return ValueTask.FromResult(SandboxValue.FromInt32(ScalarValue));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { });

    private static BindingDescriptor TagsBinding()
        => new(
            "host.probe.getTags", SemVersion.One, [SandboxType.String], SandboxType.List(SandboxType.String),
            AllocatingReadEffects, "probe.read.tags",
            BindingCostModel.Fixed(3), AuditLevel.PerResource, BindingSafety.ReadOnlyExternal,
            (context, args, _) =>
            {
                var id = ((StringValue)args[0]).Value;
                WriteAudit(context, "host.probe.getTags", "probe.read.tags", AllocatingReadEffects, args);
                var tags = string.Equals(id, "crypt", StringComparison.Ordinal) ? CryptTags : ["solo"];
                var values = new SandboxValue[tags.Length];
                for (var i = 0; i < tags.Length; i++)
                {
                    values[i] = SandboxValue.FromString(tags[i]);
                }

                return ValueTask.FromResult(SandboxValue.FromList(values));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { });

    private static BindingDescriptor IdBinding()
        => new(
            "host.probe.generateId", SemVersion.One, [SandboxType.String], SandboxType.Guid,
            AllocatingReadEffects, "probe.read.id",
            BindingCostModel.Fixed(2), AuditLevel.PerResource, BindingSafety.ReadOnlyExternal,
            (context, args, _) =>
            {
                WriteAudit(context, "host.probe.generateId", "probe.read.id", AllocatingReadEffects, args);
                return ValueTask.FromResult(SandboxValue.FromGuid(SampleEvents.SampleId));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { });

    private static void WriteAudit(
        SandboxContext context,
        string bindingId,
        string capability,
        SandboxEffect effects,
        IReadOnlyList<SandboxValue> args)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var entityId = ((StringValue)args[0]).Value;
        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "BindingCall",
            startedAt,
            true,
            BindingId: bindingId,
            CapabilityId: capability,
            Effect: effects,
            ResourceId: $"entity:{entityId}",
            Fields: context.BindingAuditFields("probe", startedAt)));
    }
}
