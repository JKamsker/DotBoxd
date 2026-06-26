namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

/// <summary>
/// Guard for a derived DTO field that references a stored field whose value is side-effectful (a <c>[HostBinding]</c>
/// host call). The derived getter inlines the bound field's lowered IR verbatim at every reference — there is no
/// let-binding in the lowered IR to evaluate it once — so referencing a host-call field more than once would emit the
/// host call multiple times (duplicate host calls, doubled budget, non-determinism). Such a chain must fail safe
/// (skipped); a single reference, or a reference to a pure (non-side-effectful) field, must still lower. Shares the
/// <see cref="RemoteRunLocalChainRuntimeTests"/> harness.
/// </summary>
public sealed partial class RemoteRunLocalChainRuntimeTests
{
    private const string DoubledHostFieldSource = HostPrelude + """
        public interface IDoubleWorld
        {
            [HostBinding("host.probe.getValue", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int GetValue(string id);
        }

        public readonly record struct DoubledValueDto(int Value)
        {
            public int Doubled => Value + Value;   // references the host-call field TWICE
        }

        public static class DoubledHostFieldUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select((e, ctx) => new DoubledValueDto(ctx.Host<IDoubleWorld>().GetValue(e.Zone)))
                    .RunLocal((dto, ctx) => { });
        }
        """;

    private const string PlusOneHostFieldSource = HostPrelude + """
        public interface IPlusWorld
        {
            [HostBinding("host.probe.getValue", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int GetValue(string id);
        }

        public readonly record struct PlusOneValueDto(int Value)
        {
            public int PlusOne => Value + 1;   // references the host-call field ONCE
        }

        public static class PlusOneHostFieldUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select((e, ctx) => new PlusOneValueDto(ctx.Host<IPlusWorld>().GetValue(e.Zone)))
                    .RunLocal((dto, ctx) => { });
        }
        """;

    private const string PureDoubledFieldSource = Prelude + """
        public readonly record struct PureDoubledDto(int Value)
        {
            public int Doubled => Value + Value;   // pure event-prop field referenced twice -> not side-effectful
        }

        public static class PureDoubledFieldUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new PureDoubledDto(e.Distance))
                    .RunLocal((dto, ctx) => { });
        }
        """;

    [Fact]
    public void Derived_field_referencing_a_host_call_field_twice_fails_safe()
    {
        // DoubledValueDto.Doubled => Value + Value references the stored field twice; Value is a [HostBinding] host
        // call. The derived getter inlines the bound field's IR verbatim per reference (no let-binding in the lowered
        // IR), so lowering would emit the host call TWICE. The chain must fail safe (skipped) rather than silently
        // re-evaluating the host read.
        _ = Compile(DoubledHostFieldSource, enableInterceptors: true);
        Assert.DoesNotContain("record.new", GeneratedSource(DoubledHostFieldSource));
    }

    [Fact]
    public void Derived_field_referencing_a_host_call_field_once_still_lowers()
    {
        // PlusOneValueDto.PlusOne => Value + 1 references the host-call field exactly once, so it is evaluated once and
        // lowers normally — the reuse guard must not over-reject a single reference.
        _ = Compile(PlusOneHostFieldSource, enableInterceptors: true);
        Assert.Contains("record.new", GeneratedSource(PlusOneHostFieldSource));
    }

    [Fact]
    public void Derived_field_referencing_a_pure_field_twice_still_lowers()
    {
        // PureDoubledDto.Doubled => Value + Value references the field twice, but Value is a pure event property (no
        // host call / side effect), so re-emitting it is harmless and the chain must still lower — no over-rejection.
        _ = Compile(PureDoubledFieldSource, enableInterceptors: true);
        Assert.Contains("record.new", GeneratedSource(PureDoubledFieldSource));
    }
}
