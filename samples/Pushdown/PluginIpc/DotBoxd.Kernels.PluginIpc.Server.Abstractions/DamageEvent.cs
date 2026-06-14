namespace DotBoxd.Kernels.PluginIpc.Server.Abstractions;

using DotBoxd.Kernels;
using DotBoxd.Plugins;

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
        new("e_DamageType", SandboxType.String),
        new("e_Amount", SandboxType.I32),
        new("e_TargetId", SandboxType.String)
    ];

    public IReadOnlyList<SandboxValue> ToSandboxValues(DamageEvent e)
        => [
            SandboxValue.FromString(e.DamageType),
            SandboxValue.FromInt32(e.Amount),
            SandboxValue.FromString(e.TargetId)
        ];
}
