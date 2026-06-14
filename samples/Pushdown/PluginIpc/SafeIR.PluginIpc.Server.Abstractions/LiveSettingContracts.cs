namespace SafeIR.PluginIpc.Server.Abstractions;

using SafeIR;
using SafeIR.Plugins;

public interface DamageSettings
{
    bool Enabled { get; set; }
    string DamageType { get; set; }
    int MinDamage { get; set; }
}

public sealed record MyEvent(int Value, string TargetId);

public sealed class MyEventAdapter : IPluginEventAdapter<MyEvent>
{
    public static MyEventAdapter Instance { get; } = new();

    public string EventName => "MyEvent";

    public IReadOnlyList<Parameter> Parameters { get; } = [
        new("e_Value", SandboxType.I32),
        new("e_TargetId", SandboxType.String)
    ];

    public IReadOnlyList<SandboxValue> ToSandboxValues(MyEvent e)
        => [
            SandboxValue.FromInt32(e.Value),
            SandboxValue.FromString(e.TargetId)
        ];
}
