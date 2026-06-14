---
id: API-0001
area: api_coherence
status: fixed_pending_verification
priority: medium
title: JSON host import extension is only exposed from an Internal namespace
dedup_key: api/json/import-extension/internal-namespace
created_at: 2026-06-12T20:36:10.9959017+00:00
created_by: api-completeness-scout
created_commit: 
updated_at: 2026-06-12T20:38:20.0319001+00:00
claimed_by: implementer
claimed_at: 2026-06-12T20:36:50.1064510+00:00
claim_branch: workflow-work
fixed_by: implementer
fixed_at: 2026-06-12T20:38:20.0319001+00:00
fixed_commit: working-tree
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# API-0001: JSON host import extension is only exposed from an Internal namespace

## Claim

The JSON serialization addon documents `SandboxHost.ImportJsonAsync(...)` as a normal public host API, but the extension method is declared under `DotBoxd.Kernels.Serialization.Json.Internal`. A consumer following the README/public API sample imports `DotBoxd.Kernels`, `DotBoxd.Hosting`, and `DotBoxd.Kernels.Runtime`, so the extension method is not discoverable or callable unless they also reference an `Internal` namespace.

## Evidence

- `README.md` Minimal Host Usage calls `await host.ImportJsonAsync(jsonIr)` with `using DotBoxd.Kernels;`, `using DotBoxd.Hosting;`, and `using DotBoxd.Kernels.Runtime;` only.
- `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md` says `ImportJsonAsync` is the extension method provided by the JSON serialization addon.
- `src/DotBoxd.Kernels.Serialization.Json/Internal/SandboxHostJsonExtensions.cs` declares `namespace DotBoxd.Kernels.Serialization.Json.Internal;` for the public `SandboxHostJsonExtensions` type.
- `tests/DotBoxd.Kernels.Tests/GlobalUsings.cs` imports `DotBoxd.Kernels.Serialization.Json.Internal`, so the current API surface test does not catch whether a normal consumer can use the documented namespace/import pattern.

## User impact

Users following the README get a compile error for `ImportJsonAsync` and must discover an internal namespace to use a documented public feature. This also makes the intended package boundary unclear because public docs point to a stable addon API while the namespace says implementation detail.

## Breaking-change risk

Moving or forwarding the extension into a public namespace is additive if the existing internal namespace is kept temporarily. Removing the old namespace immediately would be a source break for tests or early consumers that copied the internal import.

## Suggested acceptance test

Add a consumer-facing compile/API test that references `DotBoxd.Kernels.Serialization.Json` and verifies this snippet compiles without importing `.Internal`:

```csharp
using DotBoxd.Kernels;
using DotBoxd.Hosting;
using DotBoxd.Kernels.Runtime;
using DotBoxd.Kernels.Serialization.Json;

using var host = SandboxHost.Create(builder => builder.UseInterpreter());
_ = await host.ImportJsonAsync(jsonIr);
```

Also include a README-style variant if the intended public namespace is `DotBoxd.Kernels` rather than `DotBoxd.Kernels.Serialization.Json`.

## Smallest fixable slice

Expose `SandboxHostJsonExtensions` from the intended public namespace, such as `DotBoxd.Kernels.Serialization.Json`, and update tests/examples to avoid `DotBoxd.Kernels.Serialization.Json.Internal` for public addon APIs. Keep a forwarding compatibility shim only if source compatibility is desired.
