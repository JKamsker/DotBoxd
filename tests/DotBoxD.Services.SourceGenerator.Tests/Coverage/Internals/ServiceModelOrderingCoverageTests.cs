using System.Collections.Immutable;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;

namespace DotBoxD.Services.SourceGenerator.Tests.Coverage;

public sealed class ServiceModelOrderingCoverageTests
{
    private static ServiceModel Model(string @namespace, string interfaceName, string serviceName) =>
        new(
            @namespace,
            interfaceName,
            serviceName,
            EquatableArray<MethodModel>.Empty,
            EquatableArray<ServicePropertyModel>.Empty);

    [Fact]
    public void Sort_OrdersByNamespaceThenInterfaceThenService_Ordinal()
    {
        var unsorted = ImmutableArray.Create(
            Model("Zeta", "IThing", "SvcA"),
            Model("Alpha", "IBeta", "SvcA"),
            Model("Alpha", "IAlpha", "SvcB"),
            Model("Alpha", "IAlpha", "SvcA"));

        var sorted = ServiceModelOrdering.Sort(unsorted, CancellationToken.None);

        var order = sorted.ToArray()
            .Select(m => $"{m.Namespace}/{m.InterfaceName}/{m.ServiceName}")
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Alpha/IAlpha/SvcA",
                "Alpha/IAlpha/SvcB",
                "Alpha/IBeta/SvcA",
                "Zeta/IThing/SvcA",
            },
            order);
    }

    [Fact]
    public void Sort_IsOrdinal_UppercaseSortsBeforeLowercase()
    {
        var unsorted = ImmutableArray.Create(
            Model("alpha", "IFoo", "S"),
            Model("Zeta", "IFoo", "S"));

        var sorted = ServiceModelOrdering.Sort(unsorted, CancellationToken.None);

        Assert.Equal("Zeta", sorted[0].Namespace);
        Assert.Equal("alpha", sorted[1].Namespace);
    }

    [Fact]
    public void Sort_EmptyInput_ReturnsEmpty()
    {
        var sorted = ServiceModelOrdering.Sort(ImmutableArray<ServiceModel>.Empty, CancellationToken.None);

        Assert.True(sorted.IsEmpty);
        Assert.Equal(0, sorted.Count);
    }
}
