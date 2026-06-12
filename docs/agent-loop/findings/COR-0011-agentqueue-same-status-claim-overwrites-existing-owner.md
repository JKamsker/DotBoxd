---
id: COR-0011
area: correctness
status: open
priority: medium
title: AgentQueue same-status claim overwrites existing owner
dedup_key: correctness/agentqueue/transitions/same-status-claim-overwrites-owner
created_at: 2026-06-12T22:02:33.0290692+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-12T22:02:33.0290692+00:00
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

# COR-0011: AgentQueue same-status claim overwrites existing owner

## Claim

AgentQueue treats same-status transitions as valid for every status, which lets a second `claim` command overwrite an already claimed finding's owner and branch without a release or force operation.

## Evidence

`tools/AgentQueue/src/AgentQueue/Core/AgentQueueCatalog.cs` starts `CanTransition` with:

```csharp
if (current == next)
{
    return true;
}
```

`tools/AgentQueue/src/AgentQueue/Infrastructure/QueueMutationCommands.cs` uses that helper in `Claim`, then unconditionally writes the new `claimed_by`, `claimed_at`, and `claim_branch` fields. Therefore this sequence is accepted:

```powershell
./scripts/agentq.ps1 append --area correctness --priority medium --title "ownership race" --dedup-key "example/ownership" --agent auditor --body-file body.md
./scripts/agentq.ps1 claim COR-xxxx --agent fixer-a --branch fix/a
./scripts/agentq.ps1 claim COR-xxxx --agent fixer-b --branch fix/b
```

The second claim leaves the finding in `claimed` status but silently changes ownership from `fixer-a` to `fixer-b`. `QueueDoctor.CheckEvents` also accepts same-status event transitions through the same `CanTransition` helper, so the overwritten state is considered healthy.

## Suggested test

Extend AgentQueue workflow tests to append a finding, claim it as `fixer-a`, then attempt `claim <id> --agent fixer-b --branch fix/b`. The second claim should fail with `ExitCodes.InvalidTransition` and leave `claimed_by` and `claim_branch` unchanged. If idempotent retries are desired, add a separate test showing only the same agent/branch can repeat the claim without mutation.

## Expected behavior

A claimed finding should not be claimable by another agent without `release` or a dedicated forced takeover command. Same-status transitions should be allowed only where they are explicitly idempotent and do not overwrite ownership/audit metadata.

## Deduplication key

`correctness/agentqueue/transitions/same-status-claim-overwrites-owner`
