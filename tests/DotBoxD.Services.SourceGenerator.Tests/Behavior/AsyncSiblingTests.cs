using System.Reflection;
using FluentAssertions;
using static DotBoxD.Services.SourceGenerator.Tests.Behavior.AsyncSiblingTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Behavior;

/// <summary>
/// Coverage for the auto-generated async sibling interface. For every
/// <c>[RpcService]</c> interface the generator emits a sibling
/// <c>I{Name}Async</c> whose members are non-blocking. The proxy class
/// implements both interfaces.
/// </summary>
public class AsyncSiblingTests
{
    /// <summary>
    /// A sync method on the user interface produces an async counterpart on the sibling,
    /// and the proxy exposes both: the blocking original (returning T) and the awaitable
    /// sibling (returning Task&lt;T&gt;).
    /// </summary>
    [Fact]
    public async Task SyncMethod_GeneratesAsyncSibling_AndProxyImplementsBothInterfaces()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace AsyncSibling.A
            {
                [RpcService]
                public interface ICalc
                {
                    int Add(int a, int b);
                }
            }
            """;

        var (asm, _) = Compile(source);

        var sync = asm.GetType("AsyncSibling.A.ICalc")!;
        var async = asm.GetType("AsyncSibling.A.ICalcAsync");
        async.Should().NotBeNull("the generator must emit a sibling interface named ICalcAsync");

        var proxy = asm.GetType("AsyncSibling.A.CalcProxy")!;
        sync.IsAssignableFrom(proxy).Should().BeTrue();
        async!.IsAssignableFrom(proxy).Should().BeTrue("the proxy must implement both views");

        // The async sibling's AddAsync method must accept (int, int, CancellationToken).
        var siblingAdd = async.GetMethod("AddAsync")!;
        siblingAdd.ReturnType.Should().Be(typeof(Task<int>));
        siblingAdd.GetParameters().Select(p => p.ParameterType)
            .Should().BeEquivalentTo(new[] { typeof(int), typeof(int), typeof(CancellationToken) },
                "the sibling appends a CancellationToken with default value");

        // Sanity: at runtime the awaitable sibling call goes through the same wire path
        // as the blocking original.
        var recorder = new Recorder { NextResult = 42 };
        var instance = Activator.CreateInstance(proxy, recorder)!;
        var task = (Task<int>)siblingAdd.Invoke(instance, new object[] { 4, 5, CancellationToken.None })!;
        (await task).Should().Be(42);
        recorder.LastService.Should().Be("ICalc");
        recorder.LastMethod.Should().Be("Add");
    }

    /// <summary>
    /// A method whose original name already ends in <c>Async</c> and is already async with
    /// a CT parameter must NOT produce a duplicate proxy method — one implementation
    /// satisfies both interfaces.
    /// </summary>
    [Fact]
    public void AlreadyAsyncWithCt_DoesNotCauseDuplicateProxyMethod()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace AsyncSibling.B
            {
                [RpcService]
                public interface IAlready
                {
                    Task<int> FooAsync(int x, CancellationToken ct = default);
                }
            }
            """;

        var (asm, _) = Compile(source);
        var proxy = asm.GetType("AsyncSibling.B.AlreadyProxy")!;
        var fooMethods = proxy.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "FooAsync").ToArray();
        fooMethods.Should().HaveCount(1,
            "a single physical method already satisfies both IAlready and IAlreadyAsync");
    }

    /// <summary>
    /// An already-async method WITHOUT a CT must get a sibling method that adds a CT
    /// — so the proxy emits TWO physical methods.
    /// </summary>
    [Fact]
    public void AsyncWithoutCt_GeneratesSecondProxyMethodWithCt()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace AsyncSibling.C
            {
                [RpcService]
                public interface INoCt
                {
                    Task<int> FetchAsync(int id);
                }
            }
            """;

        var (asm, _) = Compile(source);
        var proxy = asm.GetType("AsyncSibling.C.NoCtProxy")!;
        var fetchMethods = proxy.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "FetchAsync").ToArray();
        fetchMethods.Should().HaveCount(2);
        fetchMethods.Should().Contain(m => m.GetParameters().Length == 1, "the original interface method");
        fetchMethods.Should().Contain(m => m.GetParameters().Length == 2
            && m.GetParameters()[1].ParameterType == typeof(CancellationToken),
            "the sibling adds CancellationToken");
    }

    /// <summary>
    /// Calling the async sibling method on the proxy must not block — the underlying
    /// IRpcInvoker call uses the awaited path, not GetAwaiter().GetResult().
    /// </summary>
    [Fact]
    public async Task SiblingCall_IsTrulyNonBlocking_NotAGetResultWrapper()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace AsyncSibling.F
            {
                [RpcService]
                public interface IBlocker
                {
                    int Slow(int x);
                }
            }
            """;

        var (asm, _) = Compile(source);

        // The recorder's invocation returns an unstarted task and only completes when
        // we explicitly signal it — so a blocking GetAwaiter().GetResult() would deadlock
        // the test, but a true async path would let us await it.
        var gate = new TaskCompletionSource<object?>();
        var recorder = new DeferredRecorder(gate.Task);
        var proxy = asm.GetType("AsyncSibling.F.BlockerProxy")!;
        var instance = Activator.CreateInstance(proxy, recorder)!;
        var siblingMethod = asm.GetType("AsyncSibling.F.IBlockerAsync")!.GetMethod("SlowAsync")!;

        // Kick off the sibling call without awaiting it; release the gate after a short
        // delay; then await the task. If the sibling were secretly blocking, the test
        // would deadlock on a single-threaded sync context — but the test pool has
        // multiple threads, so we time-bound the await as a backstop.
        var task = (Task<int>)siblingMethod.Invoke(instance, new object[] { 7, CancellationToken.None })!;
        task.IsCompleted.Should().BeFalse("the sibling call must not have synchronously completed");
        gate.SetResult(99);
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.Should().BeSameAs(task, "the sibling call must complete after the underlying client task does");
        (await task).Should().Be(99);
    }

}
