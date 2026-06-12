---
id: COR-0021
area: correctness
status: verified
priority: high
title: File write temp publication can follow a raced reparse point
dedup_key: security/file-write/temp-publication/reparse-race
created_at: 2026-06-12T22:13:42.8442526+00:00
created_by: security-producer
created_commit: 
updated_at: 2026-06-12T22:47:35.1410845+00:00
claimed_by: worker
claimed_at: 2026-06-12T22:15:47.7367750+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T22:33:50.7499314+00:00
fixed_commit: 
verified_by: independent-verifier
verified_at: 2026-06-12T22:47:35.1410845+00:00
verified_commit: 
duplicate_of: 
---

# COR-0021: File write temp publication can follow a raced reparse point

# File write temp publication can follow a raced reparse point

## Summary

`file.writeText` checks the requested final path for reparse points, but writes the temporary staging file with `File.WriteAllBytesAsync` to `finalPath + ".tmp-<guid>"` without opening that temp path with no-follow semantics or validating the temp path after open. A filesystem adversary with write access to the granted root can race the predictable directory location and redirect the temp write through a symlink/reparse point outside the sandbox root.

## Evidence

- `src/SafeIR.Runtime/Bindings/SafeFileSystem.cs` resolves the final path under the granted root and calls `EnsureNoReparsePoint(resolved.RootFull, resolved.FullPath)` before writing.
- The same method then computes `var tempPath = resolved.FullPath + ".tmp-" + Guid.NewGuid().ToString("N")` and writes with `await File.WriteAllBytesAsync(tempPath, bytes, timeout.Token)`.
- `File.WriteAllBytesAsync` follows normal filesystem semantics; it does not use the existing `SafeFileNoFollow` helper and does not open with `O_NOFOLLOW`/`FILE_FLAG_OPEN_REPARSE_POINT` style protection.
- `SafeFileSystem` rechecks only `resolved.FullPath` before `SafeFileWritePublisher.PublishTempFile`; it never checks that the temp file itself was not a reparse point and was created as a regular file under the root.
- `src/SafeIR.Runtime/SafeFileNoFollow.cs` provides no-follow protection for reads only (`OpenRead`), while there is no equivalent no-follow create/write helper for the staging file.
- Existing reparse tests in `tests/SafeIR.Tests/Misc07/SafeFileSystemReparsePointTests.cs` cover nested and terminal reparse points on the requested final path, but they do not cover a reparse point introduced at the temporary staging path between the final-path check and the write.

## Impact

When a granted root is shared with another local process, tenant workspace, watched directory, or otherwise attacker-writable location, the file capability boundary can be bypassed by racing creation of the staging path as a symlink or junction to an outside target. The sandboxed write content can then be written outside the whitelisted root even though the final requested path passes the root and reparse checks. This weakens the path/resource whitelist boundary for `file.write`.

## Security test idea

Add a controlled test hook or injectable temp-name provider so a test can pre-create or race `target.txt.tmp-known` as a symlink/junction to a file outside the root, then execute `file.writeText("target.txt", "new")`. The fixed behavior should fail with `PermissionDenied` and leave the outside file unchanged.

## Suggested fix direction

Create staging files with no-follow and create-new semantics inside the trusted root, then validate the opened handle rather than validating only the path string. On Unix, use `open` with `O_CREAT | O_EXCL | O_NOFOLLOW | O_CLOEXEC`; on Windows, use `CreateFileW` with `CREATE_NEW`, `FILE_FLAG_OPEN_REPARSE_POINT`, and reject handles whose attributes include `FILE_ATTRIBUTE_REPARSE_POINT`. Also check the parent path immediately before handle creation, and publish only a handle-created regular temp file.
