---
id: COR-0004
area: correctness
status: fixed_pending_verification
priority: high
title: HTTP allowlist entries can expand through comma-delimited grant serialization
dedup_key: correctness/http/allowlist/csv-delimiter-expansion
created_at: 2026-06-12T20:39:07.6972619+00:00
created_by: security-reviewer
created_commit: 
updated_at: 2026-06-12T20:46:36.7556778+00:00
claimed_by: implementer
claimed_at: 2026-06-12T20:43:34.0152904+00:00
claim_branch: workflow-work
fixed_by: implementer
fixed_at: 2026-06-12T20:46:36.7556778+00:00
fixed_commit: working-tree
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0004: HTTP allowlist entries can expand through comma-delimited grant serialization

## Claim

HTTP host and scheme allowlist entries are serialized as comma-separated strings without rejecting delimiters inside individual entries, so one intended allowlist value can be interpreted as multiple whitelisted authorities or schemes.

## Evidence

- `src/DotBoxd.Hosting.Http/Internal/SafeHttpPolicyBuilderExtensions.cs:7` exposes `GrantHttpGet(IEnumerable<string> allowedHosts, ...)`, which is the host-facing helper for explicit HTTP whitelisting.
- `src/DotBoxd.Hosting.Http/Internal/SafeHttpPolicyBuilderExtensions.cs:35` stores the host list as `string.Join(',', allowedHosts)` and `src/DotBoxd.Hosting.Http/Internal/SafeHttpPolicyBuilderExtensions.cs:36` does the same for schemes.
- `src/DotBoxd.Hosting.Http/Internal/SafeHttpGrantReader.cs:8` to `src/DotBoxd.Hosting.Http/Internal/SafeHttpGrantReader.cs:11` reads those parameters by splitting on commas and trimming entries.
- `src/DotBoxd.Hosting.Http/Internal/SafeHttpGrantValidator.cs:46` to `src/DotBoxd.Hosting.Http/Internal/SafeHttpGrantValidator.cs:52` only requires the CSV to contain at least one value; it does not reject comma-containing builder inputs or validate each authority/scheme token as an atomic whitelist entry.
- `src/DotBoxd.Hosting.Http/SafeHttpClient.cs:188` to `src/DotBoxd.Hosting.Http/SafeHttpClient.cs:193` authorizes a request when any parsed host token matches the URI authority.

A host that calls `GrantHttpGet(["api.example.com,evil.example"], maxResponseBytes: 1024)` has supplied one string entry, but the runtime parses it as two allowed hosts. The same delimiter expansion applies to `allowedSchemes`.

## Risk

DotBoxd.Kernels's network model relies on explicit server whitelisting. If HTTP allowlists are populated from configuration, UI fields, or tenant policy data where each item is expected to be one authority, a comma in a single item silently broadens the whitelist instead of failing closed. That can permit sandboxed IR with `net.http.get` to reach an unintended host or scheme under a policy that reviewers may believe contains only one entry.

## Suggested acceptance tests

- `SandboxPolicyBuilder.Create().GrantHttpGet(["api.example.com,evil.example"], 1024)` should fail during policy construction or `PrepareAsync` with a policy grant diagnostic, rather than allowing `https://evil.example/...`.
- `GrantHttpGet(["api.example.com"], 1024, allowedSchemes: ["https,http"])` should fail closed instead of enabling `http` as a second scheme.
- Direct `CapabilityGrant("net.http.get", ...)` policies should validate each parsed host/scheme token and reject malformed authorities, empty tokens, delimiters inside tokens, and unsupported schemes before execution.

## Expected behavior

Each allowlist element supplied through the host-facing builder should map to exactly one allowed authority or scheme. Invalid or delimiter-containing entries should be rejected before a sandbox execution plan is prepared.

## Suggested fix direction

Represent allowlists internally as structured values or add strict per-entry validation in `GrantHttpGet` and `SafeHttpGrantValidator`: reject commas in individual builder entries, reject empty or whitespace-only entries, validate host tokens as exact host or host:port authorities without URI/user-info/path characters, and validate schemes against an explicit safe scheme set such as `https` plus opt-in `http` if intentionally supported.

## Deduplication key

correctness/http/allowlist/csv-delimiter-expansion
