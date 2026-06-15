namespace DotBoxD.Kernels.PluginIpc.Server.Abstractions;

using DotBoxD.Kernels;
using DotBoxD.Plugins;

public sealed record DamageEvent(string DamageType, int Amount, string TargetId);

public interface IFireDamageSettings
{
    string DamageType { get; set; }
    int MinDamage { get; set; }
}

public sealed class DamageEventAdapter : IPluginEventValueWriter<DamageEvent>
{
    public static DamageEventAdapter Instance { get; } = new();

    public string EventName => "DamageEvent";

    public IReadOnlyList<Parameter> Parameters { get; } = [
        new("e_DamageType", SandboxType.String),
        new("e_Amount", SandboxType.I32),
        new("e_TargetId", SandboxType.String)
    ];

    public int EventValueCount => 3;

    public IReadOnlyList<SandboxValue> ToSandboxValues(DamageEvent e)
        => [
            SandboxValue.FromString(e.DamageType),
            SandboxValue.FromInt32(e.Amount),
            SandboxValue.FromString(e.TargetId)
        ];

    public SandboxValue ToSandboxValue(DamageEvent e, int index)
        => index switch
        {
            0 => SandboxValue.FromString(e.DamageType),
            1 => SandboxValue.FromInt32(e.Amount),
            2 => SandboxValue.FromString(e.TargetId),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

    public void CopySandboxValues(DamageEvent e, SandboxValue[] destination, int destinationIndex)
    {
        destination[destinationIndex] = SandboxValue.FromString(e.DamageType);
        destination[destinationIndex + 1] = SandboxValue.FromInt32(e.Amount);
        destination[destinationIndex + 2] = SandboxValue.FromString(e.TargetId);
    }
}
