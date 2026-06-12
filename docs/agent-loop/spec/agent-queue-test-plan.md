# 02 - AgentQueue Test Plan

## Test categories

### Initialization

- [ ] `init` creates all required directories.
- [ ] `init` is idempotent.
- [ ] `init` does not overwrite existing findings.
- [ ] `init` renders empty queue files.

### Frontmatter

- [ ] Read valid frontmatter.
- [ ] Write frontmatter preserving body.
- [ ] Reject missing `id`.
- [ ] Reject missing `area`.
- [ ] Reject missing `status`.
- [ ] Reject missing `priority`.
- [ ] Reject missing `title`.
- [ ] Reject missing `dedup_key`.
- [ ] Reject invalid status.
- [ ] Reject invalid priority.
- [ ] Reject invalid area.
- [ ] Reject ID/filename mismatch.

### ID generation

- [ ] First correctness finding becomes `COR-0001`.
- [ ] First perf allocation finding becomes `PAL-0001`.
- [ ] Existing `COR-0001`, `COR-0003` means next is `COR-0004`.
- [ ] Non-matching files are ignored.
- [ ] Deleted finding IDs are not reused if event file remains? Decide and test.
- [ ] Four-digit padding is used until 9999.

Recommended rule for deleted IDs:

```text
IDs are based on existing finding files only.
Do not delete finding files in normal workflow.
```

### Slug generation

- [ ] Lowercases.
- [ ] Replaces punctuation with dashes.
- [ ] Collapses dashes.
- [ ] Trims dashes.
- [ ] Handles empty title.
- [ ] Limits length.

### Append

- [ ] Creates finding file.
- [ ] Creates event file.
- [ ] Renders affected queue.
- [ ] Fails on duplicate dedup key.
- [ ] Fails on invalid area.
- [ ] Fails on invalid priority.
- [ ] Fails when body file does not exist.
- [ ] Supports `--json`.

### Claim

- [ ] Claims by ID.
- [ ] Claims next by area.
- [ ] Fails when no open finding exists.
- [ ] Fails when finding is already claimed.
- [ ] Records agent.
- [ ] Records branch.
- [ ] Appends event.
- [ ] Renders queue.

### Release

- [ ] Claimed -> open.
- [ ] Clears claim metadata.
- [ ] Requires reason.
- [ ] Fails if finding is not claimed.

### Fix

- [ ] Claimed -> fixed_pending_verification.
- [ ] Records fixed_by.
- [ ] Records fixed_at.
- [ ] Records fixed_commit.
- [ ] Requires notes.
- [ ] Fails if not claimed.
- [ ] Fails if same agent tries to verify immediately? This can be enforced in verify.

### Verify

- [ ] fixed_pending_verification -> verified.
- [ ] Records verified_by.
- [ ] Records verified_at.
- [ ] Records verified_commit.
- [ ] Records command string.
- [ ] Requires notes.
- [ ] Fails if not fixed_pending_verification.
- [ ] Fails if verifier is same as fixer unless `--allow-self-verify`.

Recommended rule:

```text
Self-verification is forbidden by default.
```

### Reopen

- [ ] fixed_pending_verification -> open.
- [ ] Requires reason.
- [ ] Appends event.
- [ ] Keeps old fixed metadata but adds reopen event.
- [ ] Clears claim metadata.

### Reject / duplicate / obsolete

- [ ] Reject requires reason.
- [ ] Duplicate requires `--of`.
- [ ] Duplicate target must exist.
- [ ] Obsolete requires reason.
- [ ] Final statuses cannot be claimed unless reopened with `--force`.

### Render

- [ ] Deterministic output.
- [ ] Stable ordering.
- [ ] Contains generated warning.
- [ ] Groups by status.
- [ ] Uses checkbox symbols correctly.
- [ ] `render --check` exits 0 when clean.
- [ ] `render --check` exits non-zero when stale.
- [ ] Deleted queue file is recreated.

### Doctor

- [ ] Detects missing directories.
- [ ] Detects malformed frontmatter.
- [ ] Detects duplicate dedup keys.
- [ ] Detects duplicate IDs.
- [ ] Detects missing event file.
- [ ] Detects invalid status transition in events where practical.
- [ ] Detects stale rendered queues.
- [ ] Provides actionable error messages.

### Locking

- [ ] Two writer commands cannot mutate at the same time in one worktree.
- [ ] Lock timeout returns exit code 5.
- [ ] Read-only commands do not require exclusive lock unless `--strict-lock` is enabled.
- [ ] Interrupted command does not leave corrupt finding file.
- [ ] Stale lock has useful diagnostic text.

### Integration smoke test

Create a temporary repo directory and run:

```bash
agentq init

cat > /tmp/finding.md <<'EOF'
## Claim

Example correctness issue.

## Evidence

Example evidence.

## Suggested test

Example test.
EOF

agentq append --area correctness --priority high --title "Example bug" --dedup-key "correctness/example/bug" --agent correctness-auditor --body-file /tmp/finding.md
agentq claim --area correctness --agent fixer --branch fix/COR-0001
agentq fix COR-0001 --agent fixer --commit abc123 --notes "Fixed example"
agentq verify COR-0001 --agent verifier --commit def456 --cmd "dotnet test" --notes "Verified example"
agentq render --check
agentq doctor
```

Expected:

```text
COR-0001 is verified
doctor succeeds
queue markdown contains [x] COR-0001
```
