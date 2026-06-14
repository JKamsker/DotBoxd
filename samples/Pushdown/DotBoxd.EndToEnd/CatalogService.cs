namespace DotBoxd.EndToEnd;

/// <summary>
/// The host service. It owns the catalog (item -> unit price) and implements both modes:
/// <list type="bullet">
///   <item><description><c>GetUnitPriceAsync</c> — the fine-grained call the naive client makes once per line.</description></item>
///   <item><description><c>ComputeCartTotalAsync</c> — the pushdown path: it computes each line subtotal from
///   its own catalog and runs the validated <see cref="CartTotalKernel"/> server-side, returning one result.</description></item>
/// </list>
/// It also counts how many remote calls it has served, which the demo uses to show the round-trip win.
/// </summary>
public sealed class CatalogService : ICatalogService, IDisposable
{
    private readonly IReadOnlyDictionary<string, int> _prices;
    private readonly CartTotalKernel _kernel;
    private int _unitPriceCalls;
    private int _cartTotalCalls;

    public CatalogService(IReadOnlyDictionary<string, int> prices)
    {
        _prices = prices ?? throw new ArgumentNullException(nameof(prices));
        _kernel = CartTotalKernel.Create();
    }

    /// <summary>Number of <see cref="GetUnitPriceAsync"/> calls served so far.</summary>
    public int UnitPriceCalls => Volatile.Read(ref _unitPriceCalls);

    /// <summary>Number of <see cref="ComputeCartTotalAsync"/> calls served so far.</summary>
    public int CartTotalCalls => Volatile.Read(ref _cartTotalCalls);

    public ValueTask<int> GetUnitPriceAsync(string itemId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _unitPriceCalls);
        return ValueTask.FromResult(PriceOf(itemId));
    }

    public async ValueTask<CartTotal> ComputeCartTotalAsync(Cart cart, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _cartTotalCalls);

        var lines = cart.Lines ?? [];

        // Host-side composition: turn each cart line into a subtotal using the host's own catalog data.
        var subtotals = new int[lines.Length];
        for (var i = 0; i < lines.Length; i++)
        {
            subtotals[i] = PriceOf(lines[i].ItemId) * lines[i].Quantity;
        }

        // The actual summation runs inside the sandboxed kernel, next to the service.
        var (total, fuelUsed) = await _kernel.RunAsync(subtotals, cancellationToken).ConfigureAwait(false);
        return new CartTotal(total, fuelUsed);
    }

    private int PriceOf(string itemId) =>
        _prices.TryGetValue(itemId, out var price)
            ? price
            : throw new KeyNotFoundException($"Unknown catalog item '{itemId}'.");

    public void Dispose() => _kernel.Dispose();
}
