using DotBoxd.Plugins;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for COR-0062: <see cref="InMemoryPluginMessageSink.Messages"/>
/// must not expose the private mutable backing list. Callers that treat the
/// returned <see cref="IReadOnlyList{T}"/> as an immutable record of plugin
/// messages must not be able to add, remove, clear, or replace entries through it.
/// </summary>
public sealed class Fix_COR_0062_Tests
{
    [Fact]
    public void Messages_cannot_be_mutated_by_casting_back_to_List()
    {
        var sink = new InMemoryPluginMessageSink();
        sink.Send("hud", "real-message");

        IReadOnlyList<PluginMessage> exposed = sink.Messages;

        // The bug: Messages returns the private List<PluginMessage> directly, so a
        // consumer can recover the mutable instance and forge history out of band.
        if (exposed is List<PluginMessage> mutable)
        {
            mutable.Add(new PluginMessage("hud", "forged-message"));
        }

        // Correct behavior: the stored history is unaffected by the attempted
        // out-of-band mutation. This fails today because the forged message lands.
        Assert.Single(sink.Messages);
        Assert.Equal("real-message", sink.Messages[0].Message);
    }

    [Fact]
    public void Messages_cannot_be_cleared_by_casting_back_to_List()
    {
        var sink = new InMemoryPluginMessageSink();
        sink.Send("hud", "first");
        sink.Send("hud", "second");

        IReadOnlyList<PluginMessage> exposed = sink.Messages;

        if (exposed is List<PluginMessage> mutable)
        {
            mutable.Clear();
        }

        // Correct behavior: history survives the attempted clear.
        Assert.Equal(2, sink.Messages.Count);
        Assert.Equal("first", sink.Messages[0].Message);
        Assert.Equal("second", sink.Messages[1].Message);
    }

    [Fact]
    public void Messages_does_not_expose_the_private_backing_list_instance()
    {
        var sink = new InMemoryPluginMessageSink();
        sink.Send("hud", "real-message");

        // A read-only history must never be downcastable to the mutable backing
        // List<PluginMessage>. Today it is, which is the root of the finding.
        Assert.False(
            sink.Messages is List<PluginMessage>,
            "Messages exposed the mutable backing List<PluginMessage> to callers.");
    }
}
