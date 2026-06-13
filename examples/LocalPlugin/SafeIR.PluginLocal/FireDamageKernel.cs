namespace SafeIR.PluginLocal;

using System.ComponentModel.DataAnnotations;
using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.Plugins;

[Plugin("fire-damage")]
public sealed partial class FireDamageKernel : IEventKernel<DamageEvent>
{
    [LiveSetting]
    public string DamageType { get; set; } = "fire";

    [LiveSetting]
    [Range(0, 10_000)]
    public int MinDamage { get; set; } = 100;

    public bool ShouldHandle(DamageEvent e, HookContext ctx)
        => e.DamageType == DamageType &&
           e.Amount >= MinDamage;

    public void Handle(DamageEvent e, HookContext ctx)
        => ctx.Messages.Send(e.TargetId, "Ouch, fire.");
}
