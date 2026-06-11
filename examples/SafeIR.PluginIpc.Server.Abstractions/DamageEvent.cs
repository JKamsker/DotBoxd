namespace SafeIR.PluginIpc.Server.Abstractions;

using SafeIR;
using SafeIR.Plugins;

public sealed record DamageEvent(string DamageType, int Amount, string TargetId);

public interface IFireDamageSettings
{
    string DamageType { get; set; }
    int MinDamage { get; set; }
}

public sealed class DamageEventAdapter : IPluginEventAdapter<DamageEvent>
{
    public static DamageEventAdapter Instance { get; } = new();

    public string EventName => "DamageEvent";

    public IReadOnlyList<Parameter> Parameters { get; } = [
        new("eventDamageType", SandboxType.String),
        new("amount", SandboxType.I32),
        new("targetId", SandboxType.String)
    ];

    public IReadOnlyList<SandboxValue> ToSandboxValues(DamageEvent e)
        => [
            SandboxValue.FromString(e.DamageType),
            SandboxValue.FromInt32(e.Amount),
            SandboxValue.FromString(e.TargetId)
        ];
}
