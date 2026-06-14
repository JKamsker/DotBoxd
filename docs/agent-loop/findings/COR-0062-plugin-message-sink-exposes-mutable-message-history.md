---
id: COR-0062
area: correctness
status: claimed
priority: medium
title: Plugin message sink exposes mutable message history
dedup_key: correctness:plugin-message-sink-readonly-history
created_at: 2026-06-13T06:28:40.6359318+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-13T07:40:53.8755941+00:00
claimed_by: implementer
claimed_at: 2026-06-13T07:40:53.8755941+00:00
claim_branch: 
fixed_by: 
fixed_at: 
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0062: Plugin message sink exposes mutable message history

## Evidence

`InMemoryPluginMessageSink.Messages` is typed as `IReadOnlyList<PluginMessage>` but returns the private mutable `List<PluginMessage>` instance directly (`src/DotBoxd.Plugins/Contracts.cs`). Any consumer can cast the property value back to `List<PluginMessage>` and add, remove, or clear messages outside the sink APIs.

## Impact

Code that relies on the sink as a read-only record of plugin messages can observe forged or deleted messages. This affects host-side diagnostics, tests, and any validation/audit code that treats the exposed `IReadOnlyList` as immutable history.

## Fix direction

Return an immutable snapshot or read-only wrapper that does not expose the backing list, and add an immutability regression test that verifies callers cannot mutate the stored message history through `Messages`.
