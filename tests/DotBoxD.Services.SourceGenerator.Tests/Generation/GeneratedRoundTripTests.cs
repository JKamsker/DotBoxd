using DotBoxD.Services.Server;
using DotBoxD.Services.SourceGenerator.Tests.Behavior;
using FluentAssertions;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

/// <summary>
/// End-to-end round-trip tests that wire a generated client proxy <em>directly</em> into the
/// matching generated server dispatcher through a real serializer. Unlike
/// <see cref="BehavioralTests"/> — which drives the proxy and the dispatcher separately with
/// hand-built payloads — these prove the two halves of generated code agree on the wire
/// format: whatever the proxy serializes, the dispatcher can deserialize, and the result the
/// dispatcher serializes is exactly what the proxy expects back. Coverage spans argument
/// counts (0/1/2/3/4), return kinds (Task, Task&lt;T&gt;, sync, void), reference-type and
/// collection payloads, enums, nullable strings, custom wire names, the unknown-method error
/// path, and multiple services compiled together.
/// </summary>
public partial class GeneratedRoundTripTests
{
    [Fact]
    public async Task ArgumentCountMatrix_ProxyAndDispatcher_AgreeOnTheWire()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace RoundTrip.Matrix
            {
                [RpcService]
                public interface IMatrix
                {
                    Task<int> NoArgsAsync(CancellationToken ct = default);
                    Task<int> OneArgAsync(int x, CancellationToken ct = default);
                    Task<int> TwoArgsAsync(int a, int b, CancellationToken ct = default);
                    Task<int> ThreeArgsAsync(int a, int b, int c, CancellationToken ct = default);
                    Task<long> FourArgsAsync(int a, int b, int c, int d, CancellationToken ct = default);
                    Task SetAsync(int x, CancellationToken ct = default);
                    Task PingAsync(CancellationToken ct = default);
                }

                public sealed class MatrixServer : IMatrix
                {
                    public int LastSet { get; private set; } = -1;
                    public int PingCount { get; private set; }

                    public Task<int> NoArgsAsync(CancellationToken ct = default) => Task.FromResult(7);
                    public Task<int> OneArgAsync(int x, CancellationToken ct = default) => Task.FromResult(x * x);
                    public Task<int> TwoArgsAsync(int a, int b, CancellationToken ct = default) => Task.FromResult(a + b);
                    public Task<int> ThreeArgsAsync(int a, int b, int c, CancellationToken ct = default) => Task.FromResult(a + b + c);
                    public Task<long> FourArgsAsync(int a, int b, int c, int d, CancellationToken ct = default) => Task.FromResult((long)a + b + c + d);
                    public Task SetAsync(int x, CancellationToken ct = default) { LastSet = x; return Task.CompletedTask; }
                    public Task PingAsync(CancellationToken ct = default) { PingCount++; return Task.CompletedTask; }
                }
            }
            """;

        var h = Harness.Build(source, "RoundTrip.Matrix.IMatrix", "RoundTrip.Matrix.MatrixServer");

        (await h.CallAsync("NoArgsAsync")).Should().Be(7,
            "the zero-argument-with-return path must select Task<TResponse> InvokeAsync(service, method, ct) and the dispatcher must answer it with no payload to deserialize");
        (await h.CallAsync("OneArgAsync", 6)).Should().Be(36,
            "the single-argument path serializes the bare value, not a 1-tuple");
        (await h.CallAsync("TwoArgsAsync", 7, 5)).Should().Be(12,
            "two arguments travel as a ValueTuple and the dispatcher must read args.Item1/Item2");
        (await h.CallAsync("ThreeArgsAsync", 1, 2, 3)).Should().Be(6,
            "three arguments must round-trip via a 3-element ValueTuple (args.Item3)");
        (await h.CallAsync("FourArgsAsync", 1, 2, 3, 4)).Should().Be(10L,
            "four arguments must round-trip via a 4-element ValueTuple (args.Item4)");

        // No-return paths: the call must actually reach the implementation, so verify the
        // observable side effect rather than a return value.
        (await h.CallAsync("SetAsync", 99)).Should().BeNull("SetAsync returns a non-generic Task");
        ((int)h.GetImplProperty("LastSet")!).Should().Be(99,
            "the one-argument-no-return path must deliver the argument to the service");

        await h.CallAsync("PingAsync");
        ((int)h.GetImplProperty("PingCount")!).Should().Be(1,
            "the zero-argument-void path must still invoke the service exactly once");
    }

    [Fact]
    public async Task ReferenceTypesCollectionsAndEnums_RoundTripThroughGeneratedCode()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;

            namespace RoundTrip.Data
            {
                public enum Color { Red = 1, Green = 2, Blue = 3 }

                public record Point(int X, int Y);

                [RpcService]
                public interface IData
                {
                    Task<Point> EchoPointAsync(Point p, CancellationToken ct = default);
                    Task<Point> CombineAsync(Point a, Point b, CancellationToken ct = default);
                    Task<List<string>> ReverseAsync(List<string> items, CancellationToken ct = default);
                    Task<int[]> DoubleAllAsync(int[] values, CancellationToken ct = default);
                    Task<Color> NextAsync(Color c, CancellationToken ct = default);
                    Task<string?> MaybeUpperAsync(string? input, CancellationToken ct = default);
                }

                public sealed class DataServer : IData
                {
                    public Task<Point> EchoPointAsync(Point p, CancellationToken ct = default) => Task.FromResult(p);
                    public Task<Point> CombineAsync(Point a, Point b, CancellationToken ct = default) => Task.FromResult(new Point(a.X + b.X, a.Y + b.Y));
                    public Task<List<string>> ReverseAsync(List<string> items, CancellationToken ct = default) { items.Reverse(); return Task.FromResult(items); }
                    public Task<int[]> DoubleAllAsync(int[] values, CancellationToken ct = default) => Task.FromResult(values.Select(v => v * 2).ToArray());
                    public Task<Color> NextAsync(Color c, CancellationToken ct = default) => Task.FromResult((Color)(((int)c % 3) + 1));
                    public Task<string?> MaybeUpperAsync(string? input, CancellationToken ct = default) => Task.FromResult(input?.ToUpperInvariant());
                }
            }
            """;

        var h = Harness.Build(source, "RoundTrip.Data.IData", "RoundTrip.Data.DataServer");
        var pointType = h.LoadType("RoundTrip.Data.Point");
        var colorType = h.LoadType("RoundTrip.Data.Color");

        // Single reference-type argument and reference-type return.
        var point = Activator.CreateInstance(pointType, 3, 4)!;
        var echoed = (await h.CallAsync("EchoPointAsync", point))!;
        ReadInt(echoed, "X").Should().Be(3);
        ReadInt(echoed, "Y").Should().Be(4);

        // Two reference-type arguments => a tuple of DTOs must round-trip.
        var a = Activator.CreateInstance(pointType, 1, 2)!;
        var b = Activator.CreateInstance(pointType, 10, 20)!;
        var combined = (await h.CallAsync("CombineAsync", a, b))!;
        ReadInt(combined, "X").Should().Be(11);
        ReadInt(combined, "Y").Should().Be(22);

        // Generic collection argument and return.
        var reversed = (List<string>)(await h.CallAsync("ReverseAsync", new List<string> { "a", "b", "c" }))!;
        reversed.Should().Equal("c", "b", "a");

        // Array argument and return.
        var doubled = (int[])(await h.CallAsync("DoubleAllAsync", new[] { 2, 3, 4 }))!;
        doubled.Should().Equal(4, 6, 8);

        // Enum argument and return.
        var next = (await h.CallAsync("NextAsync", Enum.ToObject(colorType, 2)))!; // Green -> Blue
        Convert.ToInt32(next).Should().Be(3);

        // Nullable string: both the present and the null case must survive the wire.
        (await h.CallAsync("MaybeUpperAsync", "hello")).Should().Be("HELLO");
        (await h.CallAsync("MaybeUpperAsync", new object?[] { null })).Should().BeNull(
            "a null reference-type argument must serialize and deserialize back to null");
    }

    [Fact]
    public async Task CustomServiceAndMethodWireNames_RouteCorrectlyAtRuntime()
    {
        // The dispatcher's switch is keyed on the wire name; the proxy emits that same wire
        // name. String-match tests already assert the generated literals — this proves the
        // two literals actually meet at runtime end-to-end.
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace RoundTrip.Custom
            {
                [RpcService(Name = "calc-svc")]
                public interface ICalculator
                {
                    [RpcMethod(Name = "do-add")]
                    Task<int> AddAsync(int a, int b, CancellationToken ct = default);
                }

                public sealed class CalculatorServer : ICalculator
                {
                    public Task<int> AddAsync(int a, int b, CancellationToken ct = default) => Task.FromResult(a + b);
                }
            }
            """;

        var h = Harness.Build(source, "RoundTrip.Custom.ICalculator", "RoundTrip.Custom.CalculatorServer");

        h.Dispatcher.ServiceName.Should().Be("calc-svc",
            "the dispatcher must advertise the custom [RpcService(Name=...)] wire name");
        (await h.CallAsync("AddAsync", 20, 22)).Should().Be(42,
            "the proxy must call service 'calc-svc' / method 'do-add' and the dispatcher must resolve that exact pair");
    }

    [Fact]
    public async Task DispatchAsync_WithUnknownMethod_ThrowsServiceNotFoundException()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace RoundTrip.Errors
            {
                [RpcService]
                public interface IThing
                {
                    Task<int> GetAsync(CancellationToken ct = default);
                }

                public sealed class ThingServer : IThing
                {
                    public Task<int> GetAsync(CancellationToken ct = default) => Task.FromResult(1);
                }
            }
            """;

        var h = Harness.Build(source, "RoundTrip.Errors.IThing", "RoundTrip.Errors.ThingServer");

        var ex = await Assert.ThrowsAsync<DotBoxD.Services.Exceptions.ServiceNotFoundException>(async () =>
            await h.Dispatcher.DispatchToPayloadAsync(
                "NoSuchMethod", System.ReadOnlyMemory<byte>.Empty, h.Serializer, new InstanceRegistry(), CancellationToken.None));

        ex.Message.Should().Contain("NoSuchMethod",
            "the default switch branch must name the missing method");
        ex.Message.Should().Contain("IThing",
            "the default switch branch must name the service for diagnosability");
    }

    [Fact]
    public async Task MultipleServicesInOneCompilation_EachRoundTripsIndependently()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace RoundTrip.Multi
            {
                [RpcService]
                public interface IAlpha
                {
                    Task<int> IncAsync(int x, CancellationToken ct = default);
                }

                [RpcService]
                public interface IBeta
                {
                    Task<string> GreetAsync(string who, CancellationToken ct = default);
                }

                public sealed class AlphaServer : IAlpha
                {
                    public Task<int> IncAsync(int x, CancellationToken ct = default) => Task.FromResult(x + 1);
                }

                public sealed class BetaServer : IBeta
                {
                    public Task<string> GreetAsync(string who, CancellationToken ct = default) => Task.FromResult("hi " + who);
                }
            }
            """;

        var asm = CompileAndLoad(source);
        var serializer = new TestJsonSerializer();
        var registry = new InstanceRegistry();
        var client = new LoopbackClient(serializer, registry);

        var alpha = Harness.Attach(asm, client, "RoundTrip.Multi.IAlpha", "RoundTrip.Multi.AlphaServer", serializer, registry);
        var beta = Harness.Attach(asm, client, "RoundTrip.Multi.IBeta", "RoundTrip.Multi.BetaServer", serializer, registry);

        (await alpha.CallAsync("IncAsync", 41)).Should().Be(42,
            "the first service must route through its own dispatcher");
        (await beta.CallAsync("GreetAsync", "bob")).Should().Be("hi bob",
            "the second service must route through its own dispatcher without interference");
    }

}
