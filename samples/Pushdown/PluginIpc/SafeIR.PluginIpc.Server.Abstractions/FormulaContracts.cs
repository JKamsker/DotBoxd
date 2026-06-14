namespace SafeIR.PluginIpc.Server.Abstractions;

/// <summary>
/// Host-side contract design guidance only. This formula slot is NOT a package-backed SafeIR
/// surface: the current SDK generator only lowers <c>IEventKernel&lt;TEvent&gt;</c> kernels into
/// plugin packages. Implementations run as ordinary host C# objects and cannot be uploaded,
/// validated, lowered, installed, or executed through the SafeIR plugin package boundary.
/// Use <c>IEventKernel&lt;TEvent&gt;</c> for any contract that must ship as a SafeIR package.
/// </summary>
public interface IDamageFormula
{
    int Calculate(DamageInput input);
}

public sealed record DamageInput(int BaseDamage, int Armor);
