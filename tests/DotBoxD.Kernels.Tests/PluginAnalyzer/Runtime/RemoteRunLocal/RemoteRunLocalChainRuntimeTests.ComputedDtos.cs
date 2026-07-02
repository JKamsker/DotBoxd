namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public readonly record struct CalculatedPoint(int X, int Y)
{
    public int Sum => X + Y;
}

public sealed record CalculatedPointEvent(int Distance, CalculatedPoint Point);

public sealed record CalculatedPointFlatEvent(int Distance, int X, int Y);

public sealed class OptionalConstructorProfileEvent
{
    public OptionalConstructorProfileEvent(string name, int health, int marker = 7)
    {
        _ = marker;
        Name = name;
        Health = health;
    }

    public OptionalConstructorProfileEvent(string name, int health)
    {
        Name = "wrong:" + name;
        Health = -health;
    }

    public string Name { get; }
    public int Health { get; }
    public int Rank { get; set; }
}

public sealed partial class RemoteRunLocalChainRuntimeTests
{
    private const string ComputedDtoProjectionSource = Prelude + """
        public readonly record struct CalculatedPointDto(int X, int Y)
        {
            public int Sum => X + Y;
        }

        public static class ComputedDtoProjectionUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.CalculatedPointFlatEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new CalculatedPointDto(e.X, e.Y)).RunLocal((point, ctx) => { });
        }
        """;

    private const string ComputedDtoInitializerProjectionSource = Prelude + """
        public readonly record struct CalculatedPointInitializerDto
        {
            public int X { get; init; }
            public int Y { get; init; }
            public int Sum => this.X + this.Y;
        }

        public static class ComputedDtoInitializerProjectionUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.CalculatedPointFlatEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new CalculatedPointInitializerDto { X = e.X, Y = e.Y })
                    .RunLocal((point, ctx) => { });
        }
        """;

    private const string OptionalConstructorOverloadWholeEventSource = Prelude + """
        public static class OptionalConstructorOverloadWholeEventUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.OptionalConstructorProfileEvent>().Where(e => e.Rank <= 9)
                    .RunLocal((profile, ctx) => { });
        }
        """;

    [Fact]
    public async Task Computed_dto_projection_round_trips_over_generated_payload_decoder()
    {
        var expected = new CalculatedPoint(3, 4);
        var payload = await PushFirstMatching(
            ComputedDtoProjectionSource,
            new CalculatedPointFlatEvent(3, expected.X, expected.Y),
            new CalculatedPointFlatEvent(99, 99, 1));

        Assert.Equal(expected, DecodeReflective<CalculatedPoint>(payload));
        AssertCalculatedPoint(expected, DecodeGeneratedObject(ComputedDtoProjectionSource, payload));
    }

    [Fact]
    public async Task Computed_dto_initializer_projection_round_trips_over_generated_payload_decoder()
    {
        var expected = new CalculatedPoint(5, 6);
        var payload = await PushFirstMatching(
            ComputedDtoInitializerProjectionSource,
            new CalculatedPointFlatEvent(2, expected.X, expected.Y),
            new CalculatedPointFlatEvent(99, 99, 1));

        Assert.Equal(expected, DecodeReflective<CalculatedPoint>(payload));
        AssertCalculatedPoint(expected, DecodeGeneratedObject(ComputedDtoInitializerProjectionSource, payload));
    }

    [Fact]
    public async Task Generated_payload_decoder_pins_optional_constructor_defaults_when_overloads_match()
    {
        var payload = await PushFirstMatching(
            OptionalConstructorOverloadWholeEventSource,
            new OptionalConstructorProfileEvent("hero", 3, 7) { Rank = 9 },
            new OptionalConstructorProfileEvent("filtered", 99, 7) { Rank = 99 });

        var profile = DecodeGenerated<OptionalConstructorProfileEvent>(
            OptionalConstructorOverloadWholeEventSource,
            payload);
        Assert.Equal("hero", profile.Name);
        Assert.Equal(3, profile.Health);
        Assert.Equal(9, profile.Rank);
    }

    private static void AssertCalculatedPoint(CalculatedPoint expected, object actual)
    {
        var type = actual.GetType();
        Assert.Equal(expected.X, type.GetProperty("X")!.GetValue(actual));
        Assert.Equal(expected.Y, type.GetProperty("Y")!.GetValue(actual));
        Assert.Equal(expected.Sum, type.GetProperty("Sum")!.GetValue(actual));
    }
}
