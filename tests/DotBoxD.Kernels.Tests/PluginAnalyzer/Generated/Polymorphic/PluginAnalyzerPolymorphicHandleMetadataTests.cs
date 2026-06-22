namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerPolymorphicHandleTests
{
    [Fact]
    public void Result_hook_subtype_host_binding_without_pattern_capture_fails_safe()
        => AssertFailsSafe(Source("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => new PlayerCombatant(123L).HasEquippedItem(9001L))
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """));

    [Fact]
    public void Result_hook_polymorphic_filter_with_missing_key_member_fails_safe()
        => AssertFailsSafe(Source("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Attacker is PlayerCombatant attacker &&
                                      attacker.HasEquippedItem(9001L))
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """, keyMemberExpression: "\"Missing\""));

    [Fact]
    public void Result_hook_polymorphic_filter_with_blank_key_member_fails_safe()
        => AssertFailsSafe(Source("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Attacker is PlayerCombatant attacker &&
                                      attacker.HasEquippedItem(9001L))
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """, keyMemberExpression: "\"\""));

    [Fact]
    public void Result_hook_polymorphic_filter_with_unsupported_key_type_fails_safe()
        => AssertFailsSafe(Source("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Victim is MonsterCombatant)
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """, keyType: "double"));

    [Fact]
    public void Result_hook_polymorphic_filter_with_blank_subtype_metadata_fails_safe()
        => AssertFailsSafe(Source("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Attacker is PlayerCombatant attacker &&
                                      attacker.HasEquippedItem(9001L))
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """, playerCapability: ""));

    [Fact]
    public void Result_hook_polymorphic_filter_with_blank_discriminator_fails_safe()
        => AssertFailsSafe(Source("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Attacker is PlayerCombatant attacker &&
                                      attacker.HasEquippedItem(9001L))
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """, playerDiscriminator: ""));

    [Fact]
    public void Result_hook_polymorphic_filter_with_blank_binding_prefix_fails_safe()
        => AssertFailsSafe(Source("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Attacker is PlayerCombatant attacker &&
                                      attacker.HasEquippedItem(9001L))
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """, playerBindingPrefix: ""));
}
