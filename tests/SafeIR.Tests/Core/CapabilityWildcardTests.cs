using SafeIR;

namespace SafeIR.Tests;

/// <summary>
/// Hierarchical/wildcard capability matching: a wildcard <em>grant</em> authorizes concrete required
/// capability ids beneath it, while exact grants authorize only themselves.
/// </summary>
public sealed class CapabilityWildcardTests
{
    [Theory]
    [InlineData("game.world.monster.*", "game.world.monster.health.get", true)]
    [InlineData("game.world.monster.*", "game.world.monster.health.update", true)]
    [InlineData("game.world.monster.*", "game.world.monster.list", true)]
    [InlineData("game.world.monster.*", "game.world.monster", false)]    // needs a further segment
    [InlineData("game.world.monster.*", "game.world.monsters.list", false)] // prefix boundary, not a child
    [InlineData("*", "anything.at.all", true)]
    [InlineData("game.world.monster.health.get", "game.world.monster.health.get", true)]
    [InlineData("game.world.monster.health.get", "game.world.monster.health.update", false)]
    public void Matches_respects_hierarchy(string grant, string required, bool expected)
        => Assert.Equal(expected, CapabilityPattern.Matches(grant, required));

    [Fact]
    public void Policy_wildcard_grant_authorizes_concrete_capabilities_beneath_it()
    {
        var policy = SandboxPolicyBuilder.Create()
            .Grant("game.world.monster.*", new { }, SandboxEffect.None)
            .Build();

        Assert.True(policy.GrantsCapability("game.world.monster.health.get"));
        Assert.True(policy.GrantsCapability("game.world.monster.health.update"));
        Assert.False(policy.GrantsCapability("game.world.player.health.get"));
        Assert.False(policy.GrantsCapability("game.world.monster"));
    }

    [Fact]
    public void Policy_exact_grant_does_not_authorize_siblings()
    {
        var policy = SandboxPolicyBuilder.Create()
            .Grant("game.world.monster.health.get", new { }, SandboxEffect.None)
            .Build();

        Assert.True(policy.GrantsCapability("game.world.monster.health.get"));
        Assert.False(policy.GrantsCapability("game.world.monster.health.update"));
    }

    [Fact]
    public void Policy_returns_the_wildcard_grant_for_a_matched_capability()
    {
        var policy = SandboxPolicyBuilder.Create()
            .Grant("game.world.monster.*", new { }, SandboxEffect.None)
            .Build();

        Assert.True(policy.TryGetGrant("game.world.monster.health.update", out var grant));
        Assert.Equal("game.world.monster.*", grant.Id);
    }
}
