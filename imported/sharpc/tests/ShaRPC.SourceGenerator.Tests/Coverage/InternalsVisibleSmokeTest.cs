using System.Collections.Immutable;
using ShaRPC.SourceGenerator;
using Xunit;

namespace ShaRPC.SourceGenerator.Tests.Cov;

// Smoke test: confirms InternalsVisibleTo exposes the generator's internal utilities to this
// assembly so the round-2 coverage tests can unit-test them directly.
public sealed class InternalsVisibleSmokeTest
{
    [Fact]
    public void EquatableArray_IsReachable_AndValueEqual()
    {
        var a = new EquatableArray<string>(ImmutableArray.Create("a", "b"));
        var b = new EquatableArray<string>(ImmutableArray.Create("a", "b"));
        Assert.True(a.Equals(b));
        Assert.Equal(2, a.Count);
    }
}
