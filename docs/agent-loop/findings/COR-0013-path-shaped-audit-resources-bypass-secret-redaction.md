---
id: COR-0013
area: correctness
status: open
priority: medium
title: Path-shaped audit resources bypass secret redaction
dedup_key: correctness/audit/resource-id/path-secret-redaction
created_at: 2026-06-12T22:02:46.6363317+00:00
created_by: continuous-security-producer
created_commit: 
updated_at: 2026-06-12T22:02:46.6363317+00:00
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

# COR-0013: Path-shaped audit resources bypass secret redaction

## Claim

Path-shaped audit `ResourceId` values preserve valid HTTP and file path segments verbatim, so secret-like values embedded in paths bypass the existing audit redaction rules.

## Evidence

`src/SafeIR.Transport.Http/SafeHttpClient.cs` computes `resource = SafeHttpUriAudit.Sanitize(uri.Value)` before request validation and passes that same value to audit records on success and failure. `src/SafeIR.Transport.Http/SafeHttpUriAudit.cs` implements `Sanitize` as `scheme://authority + uri.AbsolutePath`, which strips query/userinfo but keeps every path segment. A URI such as `https://api.example.com/download/token/abc123?ignored=secret` is therefore materialized as `https://api.example.com/download/token/abc123` in `SandboxAuditEvent.ResourceId`.

`src/SafeIR.Runtime/Bindings/SafeFileSystem.cs` has the same shape for file failures: `FailureResource` returns `path.RelativePath` whenever the value is a portable relative path, and `SafeFileAudit` only replaces backslashes before writing `ResourceId`. A denied read/write for `profiles/token-abc123.json` can therefore record the secret-shaped path directly.

Existing coverage confirms only narrower redaction behavior: `tests/SafeIR.Tests/Misc07/SafeNetworkTests.cs` checks that HTTP query strings are removed, and `tests/SafeIR.Tests/Misc07/SafeFileAuditRedactionTests.cs` checks invalid `../secret.txt` paths become `[invalid-path]`. I did not find coverage for secret-like but syntactically valid path segments.

## Impact

Audit streams are often exported outside the immediate sandbox trust boundary. A sandbox input can intentionally place bearer tokens, API keys, signed download IDs, or tenant secrets in valid path segments and have them persisted in audit `ResourceId` fields even when the same secret shape would be redacted from log messages or HTTP query/userinfo. This weakens the safe default that audit materialization should not leak secrets from attacker-controlled resource strings.

## Security test idea

Add a network audit test that executes `net.http.get` for `https://api.example.com/download/token/abc123?ignored=secret` under an `api.example.com` grant and asserts the `BindingCall` `ResourceId` contains neither `abc123` nor a secret-bearing path segment while still excluding the query. Add the same assertion on a denied-host path to cover failure audit.

Add a file audit test that attempts a denied or not-found valid relative path such as `profiles/token-abc123.json` and asserts the file `BindingCall` `ResourceId` does not preserve the secret-shaped segment verbatim.

## Suggested fix direction

Centralize `ResourceId` redaction for path-shaped resources instead of only sanitizing separators/query/userinfo. For HTTP, either reduce audit resource IDs to `scheme://authority` plus a redacted/path-template form, or redact segments adjacent to secret-ish markers such as `token`, `secret`, `key`, `signature`, and `credential`. For file paths, apply the same path-segment redaction before success and failure audit writes, including the valid-relative failure path.
