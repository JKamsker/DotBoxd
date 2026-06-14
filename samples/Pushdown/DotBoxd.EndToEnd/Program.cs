// DotBoxd end-to-end acceptance sample.
//
// One runnable program that proves the product thesis across all three usage modes:
//   1. Services  - a [DotBoxdService] contract hosted over a real named-pipe RPC transport.
//   2. Kernels   - a validated, sandboxed kernel executed under a policy via SandboxHost.
//   3. Pushdown  - the kernel runs server-side, composing the host service's own data, so the client
//                  submits ONE request instead of N fine-grained calls.
//
// It then asserts that the pushdown total equals the naive client-side composition and prints the
// round-trip win. Exits non-zero if the equivalence assertion fails, so it doubles as a CI gate.

using DotBoxd.EndToEnd;
using DotBoxd.Kernels.Transport.Ipc;
using DotBoxd.Services.Generated;

// A realistic catalog: item id -> unit price.
var prices = new Dictionary<string, int>(StringComparer.Ordinal)
{
    ["sword"] = 150,
    ["shield"] = 90,
    ["potion"] = 25,
    ["arrow"] = 3,
};

// The cart the customer wants to check out.
CartLine[] cartLines =
[
    new("sword", 1),   // 150
    new("shield", 2),  // 180
    new("potion", 5),  // 125
    new("arrow", 40),  // 120
];
var cart = new Cart(cartLines);

// Expected total, computed independently of both code paths, as the source of truth.
var expectedTotal = cartLines.Sum(line => prices[line.ItemId] * line.Quantity); // 575

// An unguessable pipe name (the transport requires real entropy unless you opt into dev names).
var pipeName = $"dotboxd-e2e-{Guid.NewGuid():N}";

using var service = new CatalogService(prices);

Console.WriteLine("== DotBoxd end-to-end: Services + Kernels + Pushdown ==");
Console.WriteLine($"Catalog has {prices.Count} items; cart has {cartLines.Length} lines.");
Console.WriteLine();

// ---- Stand up the host (Services mode) -----------------------------------------------------------
await using var host = DotBoxdDotBoxdRpcMessagePackIpc.ListenNamedPipe(
    pipeName,
    peer => peer.ProvideCatalogService(service));
await host.StartAsync();
Console.WriteLine($"[Services] Host listening on named pipe '{pipeName}'.");

// ---- Connect a client and exercise both paths over the wire --------------------------------------
await using var connection = await DotBoxdDotBoxdRpcMessagePackIpc.ConnectNamedPipeAsync(pipeName);
var catalog = connection.Get<ICatalogService>();

// (A) Naive composition: one remote call per cart line, summed on the client.
var naiveTotal = 0;
foreach (var line in cartLines)
{
    var unitPrice = await catalog.GetUnitPriceAsync(line.ItemId);
    naiveTotal += unitPrice * line.Quantity;
}
var naiveRemoteCalls = service.UnitPriceCalls;
Console.WriteLine(
    $"[Services] Naive client composition: {naiveTotal} " +
    $"({naiveRemoteCalls} remote calls / {naiveRemoteCalls} round-trips).");

// (B) Pushdown: one request; the host runs the validated kernel server-side over its own data.
var pushdown = await catalog.ComputeCartTotalAsync(cart);
Console.WriteLine(
    $"[Pushdown] Kernel-computed total: {pushdown.Total} " +
    $"(1 submission / 1 round-trip, kernel burned {pushdown.KernelFuelUsed} fuel).");
Console.WriteLine(
    "[Kernels]  The total above was produced by a sandboxed DotBoxd kernel validated under a " +
    "fuel + loop-iteration policy (see CartTotalKernel).");
Console.WriteLine();

// ---- Assert equivalence --------------------------------------------------------------------------
var ok = naiveTotal == expectedTotal
         && pushdown.Total == expectedTotal
         && pushdown.Total == naiveTotal;

Console.WriteLine($"Expected total (independent calculation): {expectedTotal}");
Console.WriteLine($"Round-trip win: {naiveRemoteCalls} remote calls -> 1 (pushdown).");
Console.WriteLine();

if (ok)
{
    Console.WriteLine("RESULT: PASS - pushdown equals naive composition; all three modes demonstrated.");
    return 0;
}

Console.Error.WriteLine(
    $"RESULT: FAIL - naive={naiveTotal}, pushdown={pushdown.Total}, expected={expectedTotal}.");
return 1;
