namespace DotBoxd.Kernels.PluginLocal;

using System.ComponentModel.DataAnnotations;
using DotBoxd.Kernels.PluginIpc.Server.Abstractions;
using DotBoxd.Plugins;

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
