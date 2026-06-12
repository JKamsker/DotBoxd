---
id: COR-0026
area: correctness
status: verified
priority: high
title: File write parent directory creation can follow a raced reparse point
dedup_key: security/file-write/parent-directory/reparse-race
created_at: 2026-06-12T22:23:27.3023023+00:00
created_by: security-producer
created_commit: 
updated_at: 2026-06-12T22:47:34.9779625+00:00
claimed_by: worker
claimed_at: 2026-06-12T22:24:41.1623949+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T22:33:52.0771020+00:00
fixed_commit: 
verified_by: independent-verifier
verified_at: 2026-06-12T22:47:34.9779625+00:00
verified_commit: 
duplicate_of: 
---

# COR-0026: File write parent directory creation can follow a raced reparse point

## Claim

`file.writeText` can create parent directories outside the granted root if a filesystem adversary races a parent path into a reparse point after the initial no-reparse check but before `Directory.CreateDirectory` runs.

## Evidence

`src/SafeIR.Runtime/Bindings/SafeFileSystem.cs` resolves the requested path, verifies it is under the granted root, and calls `EnsureNoReparsePoint(resolved.RootFull, resolved.FullPath)` before write setup. That check only inspects path components that exist at the time of the check.

For creates, `SafeFileSystem.WriteTextAsync` then calls `SafeFileWritePublisher.EnsureParentDirectory(resolved.RootFull, resolved.FullPath, permission)`. `src/SafeIR.Runtime/Bindings/SafeFileWritePublisher.cs` computes the parent directory and, when it does not exist and `allowCreate` is true, calls `Directory.CreateDirectory(directory)` before it calls `SafeFileSystem.EnsureNoReparsePoint(rootFull, fullPath)` again.

A local actor with write access to the granted root can race a missing intermediate component, for example `root/a`, into a symlink or junction after the first `EnsureNoReparsePoint` returns. `Directory.CreateDirectory(root/a/b)` can then follow that reparse point and create `b` outside the sandbox root. The later reparse check detects the bad path and prevents the file write, but the outside-root directory creation has already happened.

This is distinct from COR-0021, which covers the temporary file publication/write path. This issue is about parent directory creation side effects that occur before the second no-reparse validation.

## Risk

The file capability boundary promises that whitelisted roots contain all filesystem effects from sandboxed file writes. If `allowCreate` is enabled for a shared or attacker-writable root, a racing local actor can cause SafeIR to create directories outside that root even though the requested sandbox path is relative and initially validated. That weakens the path/resource whitelist and leaves externally visible filesystem mutations outside the approved tree.

## Suggested test

Add an injectable directory-creation hook or synchronization point between `ResolvePath` and `Directory.CreateDirectory`. In the test, request `a/b/out.txt` under a granted root with `allowCreate: true`, race `a` into a symlink or junction to an outside directory before parent creation, and assert the operation fails without creating `outside/b`.

## Expected behavior

Directory creation for sandbox writes should not follow newly introduced reparse points and should not create any directory outside the granted root, even under concurrent filesystem mutation.

## Suggested fix direction

Create missing parent directories one component at a time with a no-follow/open-handle strategy, validating each component immediately before and after creation. Avoid calling recursive `Directory.CreateDirectory` across untrusted missing path segments. If a component appears as a reparse point at any step, fail before creating deeper directories.

## Deduplication key

security/file-write/parent-directory/reparse-race
