---
id: PAL-0011
area: perf_alloc
status: fixed_pending_verification
priority: low
title: HTTP response reads allocate per-request buffers before decoding
dedup_key: alloc/http-response/read-buffer/memorystream-byte-buffer-per-request
created_at: 2026-06-12T22:02:50.9841836+00:00
created_by: continuous-performance-producer
created_commit: 
updated_at: 2026-06-12T22:06:50.6346019+00:00
claimed_by: fixer
claimed_at: 2026-06-12T22:05:22.1757336+00:00
claim_branch: workflow-work
fixed_by: fixer
fixed_at: 2026-06-12T22:06:50.6346019+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# PAL-0011: HTTP response reads allocate per-request buffers before decoding

## Claim

HTTP response body reading allocates a fresh `MemoryStream` and 4 KiB byte buffer per request, then copies accumulated bytes into a returned string, so small repeated `net.http.get` calls pay avoidable transient allocation unrelated to the response text itself.

## Evidence

- `src/DotBoxd.Hosting.Http/SafeHttpClient.cs` reads every successful response through `ReadLimitedTextAsync`.
- `ReadLimitedTextAsync` creates `using var memory = new MemoryStream()` and `var buffer = new byte[4096]` for each request.
- The loop writes all chunks into the `MemoryStream`, then reads `memory.GetBuffer()` and calls `Encoding.UTF8.GetString(...)` to allocate the final string.
- `PAL-0008` covers grant parameter reparsing before the request. This finding is separate: the response-read path allocates per request even when grants are cached and the response is small.
- The benchmark project currently has IPC, JSON, verifier, interpreter, map traversal, binding-reference, plugin analyzer, live-setting, and convention-adapter benchmarks, but no HTTP binding response allocation benchmark.

## Impact

Plugins or modules that perform many small allowed HTTP reads allocate at least a `MemoryStream`, a 4 KiB buffer, and any growth buffers per request before producing the unavoidable string result. In tests or in-memory transports this overhead can dominate the binding path, and in production it adds Gen0 pressure under polling-style workloads.

## Better target

Use pooled buffers or `ArrayPool<byte>` for the temporary read buffer and consider decoding incrementally with a bounded pooled byte builder. The target should make per-request overhead proportional to the returned text and metadata, not a fixed fresh 4 KiB array plus `MemoryStream` object for every response.

## Benchmark/allocation test idea

Add a BenchmarkDotNet HTTP binding benchmark with `SafeInMemoryHttpMessageInvoker` returning 0-byte, 32-byte, 1 KiB, and 64 KiB bodies across 1, 10, and 1,000 requests. Measure allocations inside request validation and body reading separately, and assert small responses do not allocate a fresh fixed-size byte buffer per call.
