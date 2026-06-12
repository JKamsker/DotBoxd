---
id: CMP-0013
area: completeness
status: open
priority: medium
title: HTTP transport lacks a runnable docs-smoke example
dedup_key: completeness/http-transport/runnable-example-smoke-missing
created_at: 2026-06-12T22:28:01.7742059+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-12T22:28:01.7742059+00:00
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

# CMP-0013: HTTP transport lacks a runnable docs-smoke example

## Claim

`SafeIR.Transport.Http` is presented as a current shipped package with HTTP GET bindings, grant helpers, pinned transport, and grant validation, but the maintained examples and docs smoke pipeline do not include a runnable HTTP transport scenario. The feature is exercised in unit tests only, so public docs can claim HTTP support without proving the consumer-facing `AddNetworkBindings()` plus `GrantHttpGet(...)` setup in a maintained example.

## Why this matters

The HTTP transport is a security-sensitive addon: hosts must configure allowlisted authorities, byte limits, DNS/private-network behavior, timeouts, and audit expectations correctly. A runnable example is the lowest-friction way for package consumers to copy the safe setup and for CI to catch documentation drift in the public HTTP surface without relying on internal test fixtures.

## Evidence

- `README.md:13` lists `SafeIR.Transport.Http` as a current package for HTTP GET binding, grant helpers, pinned transport, and HTTP grant validation.
- `src/SafeIR.Transport.Http/Hosting/SafeHttpHostBuilderExtensions.cs:8` exposes the public host setup method `AddNetworkBindings(...)`.
- `src/SafeIR.Transport.Http/Policy/SafeHttpPolicyBuilderExtensions.cs:8` exposes the public policy setup method `GrantHttpGet(...)` with allowlist, byte limit, scheme, request-byte, timeout, and private-network options.
- `docs/Specs/Initial/safe-ir-sandbox-spec/spec/08-runtime-safe-apis.md:141` through `:169` describe the safe network API requirements, including disabled-by-default networking, allowlisted schemes/hosts, DNS pinning, byte limits, wall-time timeout capping, and sanitized audit.
- `tests/SafeIR.Tests/Misc07/SafeNetworkTests.cs:22` and `tests/SafeIR.Tests/Misc04/NetworkRequestQuotaTests.cs:9` exercise HTTP behavior through test fixtures, but those are not consumer examples.
- `scripts/check-docs-smoke.ps1:8` through `:11` declares only addendum, local plugin, IPC server, and IPC client example projects; `scripts/check-docs-smoke.ps1:131` through `:132` runs only addendum and local plugin examples before the optional IPC smoke. No HTTP example project is part of docs smoke.
- Repository examples are plugin/addendum/IPC-focused; the targeted scan for `GrantHttpGet`, `AddNetworkBindings`, `SafeIR.Transport.Http`, and `net.http.get` found no example project using the HTTP transport package.
- Existing API-0006 covers a clean NuGet consumer smoke test for package layout, and API-0002 covers namespace exposure. This finding is narrower: the shipped HTTP feature lacks a maintained runnable example proving safe feature setup and behavior.

## Suggested acceptance test

Add a runnable example project or example mode that imports `SafeIR.Transport.Http`, registers `AddNetworkBindings(...)` with a deterministic/in-memory invoker, prepares a JSON module that calls `net.http.get`, grants only an explicit host through `GrantHttpGet(...)`, executes it, and prints/asserts the sanitized successful audit plus one denied-host case. Include that example in `check-docs-smoke.ps1`.

## Suggested fix direction

Create `examples/HttpTransport` or a focused docs-smoke fixture showing:

```csharp
using SafeIR.Transport.Http;

using var host = SandboxHost.Create(builder => {
    builder.AddDefaultPureBindings();
    builder.AddNetworkBindings(new SafeInMemoryHttpMessageInvoker("ok"));
});

var policy = SandboxPolicyBuilder.Create()
    .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024, timeout: TimeSpan.FromSeconds(1))
    .Build();
```

Document the expected package reference, namespace, allowlist semantics, request/response byte limits, timeout behavior, and private-network defaults. Keep the example deterministic by using the in-memory invoker, while documenting that production transport owns the real pinned network path.

## Scope boundaries

Do not change HTTP runtime behavior or grant validation as part of this finding. This is about adding a maintained consumer-facing example and docs smoke coverage for the existing public HTTP transport surface.

## Deduplication key

`completeness/http-transport/runnable-example-smoke-missing`

## Verification checklist

- [ ] A runnable HTTP transport example exists outside unit tests.
- [ ] The example uses the public `SafeIR.Transport.Http` namespace, `AddNetworkBindings(...)`, and `GrantHttpGet(...)`.
- [ ] The example demonstrates at least one allowed request and one denied unsafe/out-of-allowlist request.
- [ ] `check-docs-smoke.ps1` runs the example.
- [ ] README or runtime docs link to the example as the safe HTTP setup path.
