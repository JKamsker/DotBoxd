using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginServerDisposalRegistrationTests
{
    [Fact]
    public void RegisterEventAdapter_after_dispose_throws_object_disposed()
    {
        using var server = PluginServer.Create();
        server.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => server.RegisterEventAdapter(TestEventAdapter.Instance));
    }

    private sealed record TestEvent(string TargetId);

    private sealed class TestEventAdapter : IPluginEventAdapter<TestEvent>
    {
        public static TestEventAdapter Instance { get; } = new();

        public string EventName => nameof(TestEvent);
        public IReadOnlyList<Parameter> Parameters { get; } = [new("targetId", SandboxType.String)];
        public IReadOnlyList<SandboxValue> ToSandboxValues(TestEvent e) => [SandboxValue.FromString(e.TargetId)];
    }
}
