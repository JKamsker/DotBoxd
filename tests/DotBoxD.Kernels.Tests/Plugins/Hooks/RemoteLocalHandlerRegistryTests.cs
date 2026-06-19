using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

/// <summary>
/// The client-side reverse path for remote <c>RunLocal</c> terminals: the server runs the lowered
/// <c>Where</c>/<c>Select</c> IR and pushes the already-filtered, already-projected value as a binary payload;
/// <see cref="RemoteLocalHandlerRegistry"/> decodes it back to the projected CLR type and invokes the captured
/// native delegate. This exercises the server-extension value codec in the reverse (server-&gt;client) direction,
/// which the request/response path does not cover.
/// </summary>
public sealed class RemoteLocalHandlerRegistryTests
{
    private static HookContext Context() => new(new InMemoryPluginMessageSink(), CancellationToken.None);

    // Encodes a CLR value exactly as the server push handler would: marshal to a sandbox value, convert to the
    // wire IR, then binary-encode.
    private static byte[] EncodeProjected<T>(T value)
    {
        var sandboxValue = KernelRpcMarshaller.ToSandboxValue(value, typeof(T));
        var wire = KernelRpcValueConverter.FromSandboxValue(sandboxValue);
        return KernelRpcBinaryCodec.EncodeValue(wire);
    }

    [Fact]
    public async Task DispatchAsync_decodes_a_string_projection_and_invokes_the_native_delegate()
    {
        var registry = new RemoteLocalHandlerRegistry();
        string? observed = null;
        registry.Register<string>("sub-1", (monsterId, _) =>
        {
            observed = monsterId;
            return ValueTask.CompletedTask;
        });

        await registry.DispatchAsync("sub-1", EncodeProjected("monster-7"), Context());

        Assert.Equal("monster-7", observed);
    }

    [Fact]
    public async Task DispatchAsync_decodes_an_int_projection()
    {
        var registry = new RemoteLocalHandlerRegistry();
        var observed = 0;
        registry.Register<int>("sub-int", (value, _) =>
        {
            observed = value;
            return ValueTask.CompletedTask;
        });

        await registry.DispatchAsync("sub-int", EncodeProjected(42), Context());

        Assert.Equal(42, observed);
    }

    [Fact]
    public async Task DispatchAsync_round_trips_a_dto_record_projection()
    {
        var registry = new RemoteLocalHandlerRegistry();
        Hit? observed = null;
        registry.Register<Hit>("sub-dto", (hit, _) =>
        {
            observed = hit;
            return ValueTask.CompletedTask;
        });

        await registry.DispatchAsync("sub-dto", EncodeProjected(new Hit("player-1", 9)), Context());

        Assert.Equal(new Hit("player-1", 9), observed);
    }

    [Fact]
    public async Task DispatchAsync_can_use_a_generated_raw_payload_decoder()
    {
        var registry = new RemoteLocalHandlerRegistry();
        string? observed = null;
        byte[]? rawPayload = null;
        registry.Register(
            "sub-raw",
            (string value, HookContext _) =>
            {
                observed = value;
                return ValueTask.CompletedTask;
            },
            (Func<ReadOnlyMemory<byte>, string>)(payload =>
            {
                rawPayload = payload.ToArray();
                return "decoded";
            }));

        await registry.DispatchAsync("sub-raw", new byte[] { 255 }, Context());

        Assert.Equal("decoded", observed);
        Assert.Equal(new byte[] { 255 }, rawPayload);
    }

    [Fact]
    public async Task DispatchAsync_passes_the_supplied_context_to_the_delegate()
    {
        var registry = new RemoteLocalHandlerRegistry();
        registry.Register<string>("sub-ctx", (id, ctx) => ctx.Messages.SendAsync(id, "ack"));

        var sink = new InMemoryPluginMessageSink();
        var context = new HookContext(sink, CancellationToken.None);
        await registry.DispatchAsync("sub-ctx", EncodeProjected("m-1"), context);

        var message = Assert.Single(sink.Messages);
        Assert.Equal("m-1", message.TargetId);
        Assert.Equal("ack", message.Message);
    }

    [Fact]
    public async Task DispatchAsync_throws_when_no_handler_is_registered()
    {
        var registry = new RemoteLocalHandlerRegistry();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.DispatchAsync("missing", EncodeProjected("x"), Context()));
    }

    [Fact]
    public async Task Register_replaces_the_handler_for_a_reused_subscription_id()
    {
        // Idempotent re-registration: a plugin that reconnects and re-installs with the same id must not throw,
        // and the latest handler wins.
        var registry = new RemoteLocalHandlerRegistry();
        string? observed = null;
        var firstRegistration = registry.Register<string>("dup", (_, _) =>
        {
            observed = "first";
            return ValueTask.CompletedTask;
        });
        var secondRegistration = registry.Register<string>("dup", (value, _) =>
        {
            observed = "second:" + value;
            return ValueTask.CompletedTask;
        });

        await registry.DispatchAsync("dup", EncodeProjected("x"), Context());

        Assert.Equal("second:x", observed);

        firstRegistration.Dispose();
        observed = null;

        await registry.DispatchAsync("dup", EncodeProjected("y"), Context());

        Assert.Equal("second:y", observed);

        secondRegistration.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.DispatchAsync("dup", EncodeProjected("z"), Context()));
    }

    [Fact]
    public async Task Clear_removes_all_handlers()
    {
        var registry = new RemoteLocalHandlerRegistry();
        registry.Register<string>("a", (_, _) => ValueTask.CompletedTask);
        registry.Register<string>("b", (_, _) => ValueTask.CompletedTask);

        registry.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.DispatchAsync("a", EncodeProjected("x"), Context()));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.DispatchAsync("b", EncodeProjected("x"), Context()));
    }

    [Fact]
    public async Task Disposing_the_registration_unregisters_the_handler()
    {
        var registry = new RemoteLocalHandlerRegistry();
        var registration = registry.Register<string>("sub-x", (_, _) => ValueTask.CompletedTask);

        registration.Dispose();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.DispatchAsync("sub-x", EncodeProjected("x"), Context()));

        // The id is free again after dispose.
        registry.Register<string>("sub-x", (_, _) => ValueTask.CompletedTask);
    }

    private sealed record Hit(string AttackerId, int Damage);
}
