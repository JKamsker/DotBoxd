namespace SafeIR.Example.PluginAuthoring;

using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.Plugins;

internal static class InvalidToolingExamples
{
    public static void Describe()
    {
        Console.WriteLine("invalid tooling examples: enable INVALID_PLUGIN_EXAMPLES for SGP001 and SGP020 fixtures");
    }
}

#if INVALID_PLUGIN_EXAMPLES
[Plugin("bad-file-io")]
public sealed partial class FileIoKernel : IEventKernel<DamageEvent>
{
    public bool ShouldHandle(DamageEvent e, HookContext ctx)
    {
        System.IO.File.WriteAllText("x.txt", "bad");
        return true;
    }

    public void Handle(DamageEvent e, HookContext ctx)
        => ctx.Messages.Send(e.TargetId, "bad");
}

[Plugin("bad-live-setting")]
public sealed partial class BadLiveSettingKernel : IEventKernel<DamageEvent>
{
    [LiveSetting]
    public object Anything { get; set; } = new();

    public bool ShouldHandle(DamageEvent e, HookContext ctx)
        => true;

    public void Handle(DamageEvent e, HookContext ctx)
        => ctx.Messages.Send(e.TargetId, "bad");
}
#endif
