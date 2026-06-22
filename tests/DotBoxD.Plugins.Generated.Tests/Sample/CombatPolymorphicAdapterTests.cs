using DotBoxD.Abstractions;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Plugins.Generated.Tests.Sample;

public sealed class CombatPolymorphicAdapterTests
{
    private sealed record OrdinaryEvent(string TargetId, int Damage);

    [PolymorphicHandle(nameof(Id))]
    private sealed record StringHandle(string Id);

    private sealed record StringHandleEvent(StringHandle Target);

    [PolymorphicHandle(nameof(Id))]
    private sealed record DoubleHandle(double Id);

    private sealed record DoubleHandleEvent(DoubleHandle Target);

    [Fact]
    public void Convention_adapter_projects_handle_properties_as_keys_and_keeps_ordinary_properties()
    {
        var registry = new PluginEventAdapterRegistry();
        var ordinary = registry.Resolve<OrdinaryEvent>();
        var damage = registry.Resolve<CombatDamageContext>();

        Assert.Equal(SandboxType.String, ordinary.Parameters[0].Type);
        Assert.Equal(SandboxType.I32, ordinary.Parameters[1].Type);
        Assert.Equal(SandboxType.I64, damage.Parameters[0].Type);
        Assert.Equal(SandboxType.I64, damage.Parameters[1].Type);

        var values = damage.ToSandboxValues(new CombatDamageContext(
            new PlayerCombatant(101),
            new MonsterCombatant(202),
            CombatRelation.Pve,
            Damage: 50,
            AttackerHasDivineSword: true,
            VictimIsBoss: false,
            VictimHp: 500));

        Assert.Equal(101L, Assert.IsType<I64Value>(values[0]).Value);
        Assert.Equal(202L, Assert.IsType<I64Value>(values[1]).Value);
    }

    [Fact]
    public void Convention_adapter_rejects_null_handle_instead_of_empty_string_key()
    {
        var adapter = new PluginEventAdapterRegistry().Resolve<StringHandleEvent>();

        Assert.Throws<NotSupportedException>(() => adapter.ToSandboxValues(new StringHandleEvent(null!)));
    }

    [Fact]
    public void Convention_adapter_preserves_ordinary_null_string_behavior()
    {
        var adapter = new PluginEventAdapterRegistry().Resolve<OrdinaryEvent>();
        var values = adapter.ToSandboxValues(new OrdinaryEvent(null!, Damage: 1));

        Assert.Equal(string.Empty, Assert.IsType<StringValue>(values[0]).Value);
    }

    [Fact]
    public void Convention_adapter_rejects_unsupported_polymorphic_key_types()
        => Assert.Throws<NotSupportedException>(
            () => new PluginEventAdapterRegistry().Resolve<DoubleHandleEvent>());
}
