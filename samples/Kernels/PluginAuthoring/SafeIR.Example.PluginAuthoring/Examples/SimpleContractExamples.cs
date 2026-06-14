namespace SafeIR.Example.PluginAuthoring;

using SafeIR.PluginIpc.Server.Abstractions;

public sealed partial class EpicItemsOnly : IItemFilter
{
    public bool Accept(ItemView item, PlayerView player)
        => item.Rarity >= Rarity.Epic;
}

public sealed class ArmorAdjustedDamageFormula : IDamageFormula
{
    public int Calculate(DamageInput input)
        => Math.Max(0, input.BaseDamage - input.Armor);
}

// Host-side contract design guidance only. IItemFilter and IDamageFormula are NOT package-backed
// SafeIR surfaces: the current SDK generator only lowers IEventKernel<TEvent> kernels into plugin
// packages. The example below runs the implementations as ordinary host C# objects. It deliberately
// does not export a package, import package JSON, install it into the plugin server, or execute it
// through the sandbox host, because filters/formulas cannot ship through the SafeIR
// upload/install/execute path today.
// For a runnable package-backed surface, see KernelClassExample.cs and JsonUploadExample.cs.
internal static class SimpleContractExamples
{
    public static void Run()
    {
        Console.WriteLine(
            "simple filter/formula: host-side contract design guidance only (not a SafeIR plugin package)");

        IItemFilter filter = new EpicItemsOnly();
        IDamageFormula formula = new ArmorAdjustedDamageFormula();

        var accepted = filter.Accept(
            new ItemView("ember-crown", Rarity.Epic),
            new PlayerView("player-1", 35));
        var damage = formula.Calculate(new DamageInput(120, 25));

        Console.WriteLine($"simple filter: accepted={accepted}");
        Console.WriteLine($"damage formula: finalDamage={damage}");
    }
}
