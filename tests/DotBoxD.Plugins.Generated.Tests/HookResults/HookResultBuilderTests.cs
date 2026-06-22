using DotBoxD.Abstractions;

namespace DotBoxD.Plugins.Generated.Tests.HookResults;

// A [HookResult] record the source generator emits Ok()/Reject()/With<Field>() for at build time.
[HookResult]
public readonly partial record struct SampleHookResult(
    bool Success,
    string? Reason,
    int? Damage = null,
    bool? CanDie = null);

public sealed class HookResultBuilderTests
{
    [Fact]
    public void Ok_sets_success_and_leaves_domain_fields_default()
    {
        var result = SampleHookResult.Ok();

        Assert.True(result.Success);
        Assert.Null(result.Reason);
        Assert.Null(result.Damage);
        Assert.Null(result.CanDie);
    }

    [Fact]
    public void Reject_sets_failure_with_reason()
    {
        var result = SampleHookResult.Reject("not applicable");

        Assert.False(result.Success);
        Assert.Equal("not applicable", result.Reason);
    }

    [Fact]
    public void Reject_without_reason_sets_failure_and_null_reason()
    {
        var result = SampleHookResult.Reject();

        Assert.False(result.Success);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void With_field_returns_a_copy_with_only_that_field_changed()
    {
        var result = SampleHookResult.Ok().WithDamage(999);

        Assert.True(result.Success);
        Assert.Equal(999, result.Damage);
        Assert.Null(result.CanDie);
    }

    [Fact]
    public void With_fields_chain_independently()
    {
        var result = SampleHookResult.Ok().WithDamage(7).WithCanDie(false);

        Assert.True(result.Success);
        Assert.Equal(7, result.Damage);
        Assert.False(result.CanDie);
    }

    [Fact]
    public void Builders_do_not_mutate_the_source_value()
    {
        var ok = SampleHookResult.Ok();
        _ = ok.WithDamage(123);

        Assert.Null(ok.Damage);
    }
}
