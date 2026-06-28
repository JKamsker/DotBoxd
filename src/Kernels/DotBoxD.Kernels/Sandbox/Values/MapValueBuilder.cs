namespace DotBoxD.Kernels.Sandbox.Values;

internal sealed class MapValueBuilder
{
    private Dictionary<SandboxValue, SandboxValue>? _values;

    public MapValueBuilder(int capacity = 0)
        => _values = capacity > 0
            ? new Dictionary<SandboxValue, SandboxValue>(capacity)
            : new Dictionary<SandboxValue, SandboxValue>();

    public void Set(SandboxValue key, SandboxValue value)
        => Values[key] = value;

    internal Dictionary<SandboxValue, SandboxValue> Consume()
    {
        var values = _values ?? throw Consumed();
        _values = null;
        return values;
    }

    private Dictionary<SandboxValue, SandboxValue> Values
        => _values ?? throw Consumed();

    private static InvalidOperationException Consumed()
        => new("MapValueBuilder has already been consumed.");
}
