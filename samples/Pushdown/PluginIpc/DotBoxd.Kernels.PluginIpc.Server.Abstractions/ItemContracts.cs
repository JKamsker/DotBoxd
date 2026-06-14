namespace DotBoxd.Kernels.PluginIpc.Server.Abstractions;

/// <summary>
/// Host-side contract design guidance only. This filter slot is NOT a package-backed DotBoxd.Kernels
/// surface: the current SDK generator only lowers <c>IEventKernel&lt;TEvent&gt;</c> kernels into
/// plugin packages. Implementations run as ordinary host C# objects and cannot be uploaded,
/// validated, lowered, installed, or executed through the DotBoxd.Kernels plugin package boundary.
/// Use <c>IEventKernel&lt;TEvent&gt;</c> for any contract that must ship as a DotBoxd.Kernels package.
/// </summary>
public interface IItemFilter
{
    bool Accept(ItemView item, PlayerView player);
}

public sealed record ItemView(string Id, Rarity Rarity);

public sealed record PlayerView(string Id, int Level);

public enum Rarity
{
    Common,
    Rare,
    Epic,
    Legendary
}
