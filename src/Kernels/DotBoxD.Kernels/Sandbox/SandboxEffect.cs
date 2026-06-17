namespace DotBoxD.Kernels.Sandbox;

[Flags]
public enum SandboxEffect
{
    None = 0,
    Cpu = 1 << 0,
    Alloc = 1 << 1,
    Time = 1 << 2,
    Random = 1 << 3,
    FileRead = 1 << 4,
    FileWrite = 1 << 5,
    Network = 1 << 6,
    HostStateRead = 1 << 7,
    HostStateWrite = 1 << 8,
    Concurrency = 1 << 9,
    Audit = 1 << 11
}

public static class SandboxEffects
{
    public const SandboxEffect Pure = SandboxEffect.Cpu | SandboxEffect.Alloc;

    public static bool ContainsOnlyKnownBits(this SandboxEffect effects)
    {
        const SandboxEffect allKnown =
            SandboxEffect.Cpu |
            SandboxEffect.Alloc |
            SandboxEffect.Time |
            SandboxEffect.Random |
            SandboxEffect.FileRead |
            SandboxEffect.FileWrite |
            SandboxEffect.Network |
            SandboxEffect.HostStateRead |
            SandboxEffect.HostStateWrite |
            SandboxEffect.Concurrency |
            SandboxEffect.Audit;

        return (effects & ~allKnown) == SandboxEffect.None;
    }

    public static bool RequiresCapability(this SandboxEffect effects)
    {
        var effectful = effects & ~(SandboxEffect.Cpu | SandboxEffect.Alloc);
        return effectful != SandboxEffect.None;
    }
}
