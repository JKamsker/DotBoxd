namespace DotBoxd.EndToEnd;

using DotBoxd.Services.Attributes;
using MessagePack;

/// <summary>
/// The single host service surface for the end-to-end demo. It is a [DotBoxdService] contract, so the
/// DotBoxd source generator emits a proxy (<c>GetCatalogService</c>) for clients and a dispatcher
/// (<c>ProvideCatalogService</c>) for the host.
/// </summary>
[DotBoxdService]
public interface ICatalogService
{
    /// <summary>
    /// Returns the unit price of a single catalog item. The naive client calls this once per cart line
    /// (N remote calls / N round-trips) and then sums the cart itself.
    /// </summary>
    ValueTask<int> GetUnitPriceAsync(string itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushdown entry point: the client submits the whole cart in one request. The host composes its own
    /// catalog data and runs a validated DotBoxd kernel (under a sandbox policy) to compute the total
    /// server-side, returning a single result (1 submission / 1 round-trip).
    /// </summary>
    ValueTask<CartTotal> ComputeCartTotalAsync(Cart cart, CancellationToken cancellationToken = default);
}

/// <summary>A cart line: an item id and the quantity ordered.</summary>
[MessagePackObject]
public readonly struct CartLine
{
    [SerializationConstructor]
    public CartLine(string itemId, int quantity)
    {
        ItemId = itemId;
        Quantity = quantity;
    }

    [Key(0)]
    public string ItemId { get; }

    [Key(1)]
    public int Quantity { get; }
}

/// <summary>A cart: the set of lines the customer wants to check out.</summary>
[MessagePackObject]
public readonly struct Cart
{
    [SerializationConstructor]
    public Cart(CartLine[] lines)
    {
        Lines = lines;
    }

    [Key(0)]
    public CartLine[] Lines { get; }
}

/// <summary>The pushdown result: the computed total plus how much sandbox fuel the kernel burned.</summary>
[MessagePackObject]
public readonly struct CartTotal
{
    [SerializationConstructor]
    public CartTotal(int total, long kernelFuelUsed)
    {
        Total = total;
        KernelFuelUsed = kernelFuelUsed;
    }

    [Key(0)]
    public int Total { get; }

    [Key(1)]
    public long KernelFuelUsed { get; }
}
