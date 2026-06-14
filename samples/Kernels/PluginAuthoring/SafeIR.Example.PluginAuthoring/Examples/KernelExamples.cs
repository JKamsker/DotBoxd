namespace SafeIR.Example.PluginAuthoring;

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

    [LiveSetting]
    public bool Enabled { get; set; } = true;

    public bool ShouldHandle(DamageEvent e, HookContext context)
        => Enabled &&
           e.DamageType == DamageType &&
           e.Amount >= MinDamage;

    public void Handle(DamageEvent e, HookContext context)
        => context.Messages.Send(e.TargetId, "Fire damage detected.");
}

[Plugin("threshold-guidance")]
public sealed partial class MyKernel : IEventKernel<MyEvent>
{
    [LiveSetting]
    [Range(0, 10_000)]
    public int Threshold { get; set; } = 100;

    public bool ShouldHandle(MyEvent e, HookContext ctx)
        => e.Value > Threshold;

    public void Handle(MyEvent e, HookContext ctx)
        => ctx.Messages.Send(e.TargetId, "Threshold exceeded.");
}
