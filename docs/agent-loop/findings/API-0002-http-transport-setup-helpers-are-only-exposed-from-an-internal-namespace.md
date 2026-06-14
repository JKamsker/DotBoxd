---
id: API-0002
area: api_coherence
status: fixed_pending_verification
priority: medium
title: HTTP transport setup helpers are only exposed from an Internal namespace
dedup_key: api/http/setup-extensions/internal-namespace
created_at: 2026-06-12T20:36:12.2776467+00:00
created_by: api-completeness-scout
created_commit: 
updated_at: 2026-06-12T20:58:06.4797304+00:00
claimed_by: implementer
claimed_at: 2026-06-12T20:55:41.4958207+00:00
claim_branch: workflow-work
fixed_by: implementer
fixed_at: 2026-06-12T20:58:06.4797304+00:00
fixed_commit: working-tree
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# API-0002: HTTP transport setup helpers are only exposed from an Internal namespace

## Claim

The HTTP transport addon is described as providing public HTTP GET binding and grant helpers, but the host/policy extension methods are declared under `DotBoxd.Hosting.Http.Internal`. A normal consumer cannot discover `builder.AddNetworkBindings(...)` or `SandboxPolicyBuilder.Create().GrantHttpGet(...)` through a public addon namespace.

## Evidence

- `README.md` lists `DotBoxd.Hosting.Http` as the package for HTTP GET binding, grant helpers, pinned transport, and HTTP grant validation.
- `src/DotBoxd.Hosting.Http/Internal/SafeHttpHostBuilderExtensions.cs` declares `namespace DotBoxd.Hosting.Http.Internal;` for public `AddNetworkBindings` on `SandboxHostBuilder`.
- `src/DotBoxd.Hosting.Http/Internal/SafeHttpPolicyBuilderExtensions.cs` declares `namespace DotBoxd.Hosting.Http.Internal;` for public `GrantHttpGet` on `SandboxPolicyBuilder`.
- `tests/DotBoxd.Kernels.Tests/GlobalUsings.cs` imports `DotBoxd.Hosting.Http.Internal`, and network tests call `GrantHttpGet(...)`, so the tests exercise behavior only after opting into the internal namespace.

## User impact

Hosts attempting to enable documented HTTP transport support have to import an `Internal` namespace to access the public setup helpers. That weakens discoverability, makes samples harder to write honestly, and blurs which HTTP APIs are stable versus implementation detail.

## Breaking-change risk

Adding public namespace extension wrappers is additive. Moving the existing types without a shim would be a source break for existing internal-namespace consumers.

## Suggested acceptance test

Add a consumer-facing compile/API test that references the HTTP addon and verifies this setup compiles without importing `.Internal`:

```csharp
using DotBoxd.Kernels;
using DotBoxd.Hosting;
using DotBoxd.Hosting.Http;

using var host = SandboxHost.Create(builder => builder.AddNetworkBindings());
var policy = SandboxPolicyBuilder.Create()
    .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
    .Build();
```

The test should fail if `DotBoxd.Hosting.Http.Internal` is required for host or policy extension methods.

## Smallest fixable slice

Expose the HTTP host and policy extension methods from `DotBoxd.Hosting.Http`, then update tests/examples to import that namespace instead of `DotBoxd.Hosting.Http.Internal`. Keep internal implementation types internal or hidden behind forwarding extension wrappers.
