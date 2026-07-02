using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Plugins.Runtime;

public sealed class PluginEventAdapterMetadataValidationTests
{
    [Fact]
    public void RegisterEventAdapter_rejects_null_parameters()
        => AssertInvalidMetadataRejected(new InvalidMetadataAdapter(nameof(AdapterMetadataEvent), null!));

    [Fact]
    public void RegisterEventAdapter_rejects_null_event_name()
        => AssertInvalidMetadataRejected(new InvalidMetadataAdapter(null!, ValidParameters));

    [Fact]
    public void RegisterEventAdapter_rejects_empty_event_name()
        => AssertInvalidMetadataRejected(new InvalidMetadataAdapter(string.Empty, ValidParameters));

    [Fact]
    public void RegisterEventAdapter_rejects_null_parameter_name()
        => AssertInvalidMetadataRejected(
            new InvalidMetadataAdapter(nameof(AdapterMetadataEvent), [new Parameter(null!, SandboxType.String)]));

    [Fact]
    public void RegisterEventAdapter_rejects_null_parameter_element()
        => AssertInvalidMetadataRejected(
            new InvalidMetadataAdapter(nameof(AdapterMetadataEvent), [null!]));

    private static Parameter[] ValidParameters { get; } = [new("e_Value", SandboxType.String)];

    private static void AssertInvalidMetadataRejected(IPluginEventAdapter<AdapterMetadataEvent> adapter)
    {
        var server = DotBoxD.Plugins.PluginServer.Create();

        var ex = Assert.Throws<SandboxValidationException>(() => server.RegisterEventAdapter(adapter));

        Assert.Contains(ex.Diagnostics, diagnostic => diagnostic.Code == "DBXK036");
    }

    private sealed record AdapterMetadataEvent(string Value);

    private sealed class InvalidMetadataAdapter(
        string eventName,
        IReadOnlyList<Parameter> parameters) : IPluginEventAdapter<AdapterMetadataEvent>
    {
        public string EventName { get; } = eventName;

        public IReadOnlyList<Parameter> Parameters { get; } = parameters;

        public IReadOnlyList<SandboxValue> ToSandboxValues(AdapterMetadataEvent e)
            => [SandboxValue.FromString(e.Value)];
    }
}
