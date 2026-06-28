# DotBoxD Services Benchmark History

All results below are local stopwatch probes on this machine, run in Release mode.

## Commands

```text
dotnet run -c Release --project benchmarks/DotBoxD.Services.Benchmarks -p:UseSharedCompilation=false -- --probe-peer-proxy-cache
dotnet run -c Release --project benchmarks/DotBoxD.Services.Benchmarks -p:UseSharedCompilation=false -- --probe-stream-connection-receive-tracking
dotnet run -c Release --project benchmarks/DotBoxD.Services.Benchmarks -p:UseSharedCompilation=false -- --filter "*PeerRoundTripBenchmarks.MovePlayerAsync*" --job Short --warmupCount 1 --iterationCount 3
```

## Ledger

| Change | Commit | Probe | Result |
| --- | --- | --- | --- |
| Root RPC proxy cache | `7126981` | `--probe-peer-proxy-cache` | Repeated locked proxy creation measured 33.5 ms / 32,000,040 B for 1,000,000 calls; cached `RpcPeer.Get<IGameService>` measured 36.9 ms / 40 B. This is an allocation-only win for the small root proxy in the probe, with registry-stamp invalidation preserving replacement registration behavior. |
| Owned stream receive tracking | `4ec4439` | `--probe-stream-connection-receive-tracking` | Owned empty-stream receives with legacy active-receive tracking simulated around the current body measured 124.2 ms / 72,001,584 B for 1,000,000 calls; current owned receives measured 113.8 ms / 72,000,040 B. Non-owned streams still track active receives for blocked-read shutdown. |
| Generated Get extension proxy cache | this commit | `--probe-peer-proxy-cache` | Repeated generated `GetGameService` calls used to directly construct `GameServiceProxy`, measuring 8.6 ms / 32,000,040 B for 1,000,000 calls. Routing generated extensions through cached `RpcPeer.Get<IGameService>` measured 37.9 ms / 40 B, so this is an allocation-only tradeoff and preserves shared root proxy identity. |
| Unary frame-channel ownership | this commit | `PeerRoundTripBenchmarks.MovePlayerAsync` | Before this fix, the default profile row failed before measurement with `ObjectDisposedException: PooledBufferWriter` while `EndToEndLowAllocationProfile=true` still completed. After removing the post-transfer dispose, the same short BDN command completed both rows: default profile 3.769 us / 424 B, low-allocation profile 5.180 us / 104 B. This proves the benchmark blocker is gone; it is not a latency-improvement claim. |
