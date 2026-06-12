---
id: PAL-0027
area: perf_alloc
status: open
priority: medium
title: Audit sanitizer clones and regex-scans clean log messages
dedup_key: alloc/audit-sanitizer/clean-message-regex
created_at: 2026-06-12T22:27:04.5206368+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:27:04.5206368+00:00
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

# PAL-0027: Audit sanitizer clones and regex-scans clean log messages

## Claim

Audit/log text sanitization allocates and runs every redaction regex even when the message is already clean. The per-call log binding always pays a full string clone plus multiple regex replacement passes before writing the audit event.

## Evidence

- `src/SafeIR.Runtime/Bindings/SafeLogBindings.cs:30` calls `AuditTextSanitizer.SanitizeAndRedact(message)` for every `log.info`/`log.warn` call.
- `src/SafeIR.Runtime/AuditTextSanitizer.cs:11` starts by calling `message.ToCharArray()`, allocating a full character array for every message.
- `src/SafeIR.Runtime/AuditTextSanitizer.cs:19` then constructs a new `string` from that array even if no control characters were present.
- `src/SafeIR.Runtime/AuditTextSanitizer.cs:20` through `src/SafeIR.Runtime/AuditTextSanitizer.cs:30` run four regex replacement passes (`UriCredentialPattern`, `AuthorizationHeaderPattern`, `SecretPattern`, and `AuthSchemePattern`) regardless of whether the text contains likely secret markers.
- `src/SafeIR.Runtime/Bindings/SafeLogBindings.cs:31` and `src/SafeIR.Runtime/Bindings/SafeLogBindings.cs:32` then meter the sanitized string, so this sits directly on the log binding hot path.
- Existing COR-0013/COR-0019 cover redaction correctness and PAL-0024 covers audit-field dictionary duplication; this finding is specifically the avoidable clean-message allocation/regex cost in sanitizer execution.

## Impact

Sandbox logging can be enabled per call. Clean operational messages dominate normal logs, yet each message allocates a char array and string of message length and invokes multiple regex engines. This makes log-heavy safe workloads pay allocation and CPU costs unrelated to actual redaction work.

## Better target

Add fast paths before cloning/replacing: scan for control characters and secret marker characters/keywords, return the original string when no sanitization is needed, and run regex replacements only after a cheap prefilter indicates a possible credential/secret token. Preserve the current conservative behavior for suspicious text.

## Benchmark/allocation test idea

Benchmark `SafeLogBindings`/`AuditTextSanitizer.SanitizeAndRedact` with clean short messages, clean 4 KB messages, and messages containing one redaction. Assert clean messages avoid full-length clone allocation and do not run all regex replacements.
