---
id: PAL-0022
area: perf_alloc
status: open
priority: medium
title: File grants are reparsed on every file binding call
dedup_key: alloc/file-grants/runtime/parameter-reparse-per-call
created_at: 2026-06-12T22:19:20.9663104+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:19:20.9663104+00:00
claimed_by: 
claimed_at: 
claim_branch: 
fixed_by: 
fixed_at: 
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# PAL-0022: File grants are reparsed on every file binding call

## Claim

File capability grants are reparsed from string parameters on every `file.readText` and `file.writeText` binding call, including per-call CSV splitting for allowed extensions and repeated numeric/boolean parsing for stable grant settings.

## Evidence

- `src/DotBoxd.Kernels.Runtime/Bindings/SafeFileSystem.cs:17` calls `ResolvePath` for every read, and `src/DotBoxd.Kernels.Runtime/Bindings/SafeFileSystem.cs:75` calls the same resolver for every write.
- `src/DotBoxd.Kernels.Runtime/Bindings/SafeFileSystem.cs:139` fetches the `CapabilityGrant` from the context for each file operation, then the resolver validates root/path/extension from raw grant parameters.
- `src/DotBoxd.Kernels.Runtime/Bindings/SafeFileSystem.cs:267` reads the `allowedExtensions` parameter on every operation.
- `src/DotBoxd.Kernels.Runtime/Bindings/SafeFileSystem.cs:273` splits the comma-delimited extension string into a fresh array, and `src/DotBoxd.Kernels.Runtime/Bindings/SafeFileSystem.cs:274` scans that array with `Any` for every file read/write.
- `src/DotBoxd.Kernels.Runtime/Bindings/SafeFileSystem.cs:27` reparses `maxBytesPerRun` for reads through `ReadLong`, whose implementation at `src/DotBoxd.Kernels.Runtime/Bindings/SafeFileSystem.cs:294` through `src/DotBoxd.Kernels.Runtime/Bindings/SafeFileSystem.cs:306` reads the raw string and calls `long.TryParse` per call.
- `src/DotBoxd.Kernels.Runtime/Bindings/SafeFileWritePublisher.cs:13` and `src/DotBoxd.Kernels.Runtime/Bindings/SafeFileWritePublisher.cs:14` reparse `allowCreate` and `allowOverwrite` with `bool.TryParse` for every write.
- `src/DotBoxd.Kernels.Runtime/Bindings/SafeFileWritePublisher.cs:24` reparses write `maxBytesPerRun`, with the parser at `src/DotBoxd.Kernels.Runtime/Bindings/SafeFileWritePublisher.cs:84` through `src/DotBoxd.Kernels.Runtime/Bindings/SafeFileWritePublisher.cs:107` reading raw strings on the write hot path.
- This is distinct from `PAL-0008`, which covers HTTP grants being reparsed into request-time sets. The same stable-policy problem exists for file grants but in a different binding stack and with a separate per-call extension split.

## Impact

A sandbox that performs many small file reads or writes under the same policy pays repeated string dictionary lookups, numeric/boolean parsing, and extension-list array allocation even though grant parameters cannot change during the run. The extension split is especially visible for workloads that read many small files, where policy parsing can become a measurable part of each binding call before any file I/O is performed.

## Better target

Decode file grant parameters once during policy validation, execution-plan preparation, or binding setup into a typed immutable file-grant options object: normalized root, parsed byte limit, parsed create/overwrite flags, and a case-insensitive extension set or sorted array. Runtime file bindings should reuse that typed grant state instead of reparsing raw `CapabilityGrant.Parameters` strings per operation.

## Benchmark idea

Add a file-binding benchmark with one stable `file.read`/`file.write` grant and 1,000 to 100,000 small read/write operations using an `allowedExtensions` list of 1, 10, and 100 entries. Measure allocated bytes and elapsed time in the binding path, separating policy parsing overhead from actual file I/O where practical.
