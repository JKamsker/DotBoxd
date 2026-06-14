# Concurrency & cleanup fixes — provenance and proof

These fixes came out of an adversarial code review (a "hunt → independently verify each finding"
pass). Each was confirmed by tracing the code. They were **initially** not reproducible by a
deterministic, non-flaky test, so this file documents — for every one — exactly what was changed, why,
the evidence, and how it is now tested. They were also subjected to a round of independent multi-lens
analysis (see [Independent verification](#independent-verification)).

> **Why a passing test is not proof.** A regression test written *after* the fix and only run against the
> fixed code proves nothing — it may pass regardless of the fix. For a race the gold standard is a test
> that **fails on the unfixed code and passes on the fixed code**.

**Final proof status — every defect is now backed by a red→green test** (verified by reverting each fix
and observing the test fail), except the dead-code removal which is behaviour-preserving:

| Fix | How it is proven |
|-----|------------------|
| #1 RpcHost start token | red→green regression test |
| #3 NamedPipe `_stopCts` | red→green **stress** test (`BugFixStressTests`, threw `NullReferenceException` on iteration 1 of the reverted build) |
| #4 transport exception type | red→green regression tests |
| #7 generator default values | red→green regression test |
| #11 MessagePack null resolver | red→green regression test |
| **#2, #5, #6, #10** | red→green **deterministic seam-based** tests (`RaceConditionDeterministicTests`) — each verified to fail on the reverted code |
| #8/#9 dead branch | behaviour-preserving removal — nothing to red-test |

The four races that resisted plain testing were made deterministically testable with **minimal internal
test seams** (exposed via `InternalsVisibleTo("ShaRPC.Tests")`): `RpcPeer.HasStarted` (#2), a
connect-published hook on `NamedPipeClientTransport` (#5), an atomic `ClaimPendingAccept` consume with a
seam on `TcpServerTransport` (#6), and `EvictFaultedAttempt` + attempt accessors on the catalog (#10).
The earlier `BugFixStressTests` guards for #2/#6 are kept as cheap concurrency smoke tests; the
deterministic tests above are the actual proofs.

In addition, **#5 (and its `TcpTransport` sibling) were hardened** with an `Interlocked.MemoryBarrier()`
between publishing the connection and re-reading the disposed flag, closing the weak-memory (store-load /
Dekker) residual hole that independent review flagged.

---

## #2 — `RpcHost`: `PeerConnected` could be raised after `PeerDisconnected`

**Location:** `src/ShaRPC.Core/RpcHost.cs`, `AddPeerAsync`.

**Defect.** `peer.Start()` (which launches the read loop on the thread pool via `Task.Run`) was called
*inside* `_lifecycleLock`, but the `PeerConnected` event was raised *after* the lock was released. For a
connection whose remote end is already closed, the read loop's first `ReceiveAsync` returns
`Payload.Empty`, ending the loop and firing `Disconnected → PeerDisconnected`. That `PeerDisconnected`
could be observed **before** `PeerConnected` for the same peer.

**Fix.** Register the peer under the lock, raise `PeerConnected`, *then* call `peer.Start()`:

```diff
             if (registered)
             {
                 _peers.Add(peer);
-                peer.Start();
             }
         }
         ...
         RpcEventHandlerInvoker.Raise(PeerConnected, this, new RpcPeerEventArgs(peer));
+        peer.Start();
```

**Why the fix is safe.** `StopCoreAsync` calls `DrainInFlightAsync()` (which awaits the in-flight
`AddPeerAsync` hand-off) *before* `CloseAllAsync()`, so the peer is still started before the host drains
and closes its peers. The peer is freshly created and not disposed at this point, so `peer.Start()`
cannot throw `ObjectDisposedException`.

**Why not deterministically testable.** The read loop's `Task.Run` scheduling latency is far larger than
the lock-release-to-event-raise window, so `PeerConnected` wins the race essentially every time even on
the unfixed code. A 150-iteration in-memory stress test (`BugFixStressTests`) did **not** reproduce the
inversion. The fix makes the ordering a hard guarantee rather than a probabilistic one.

**Proven by:** `RaceConditionDeterministicTests.RpcHost_PeerConnected_FiresBeforeReadLoopStarts` — in the
`PeerConnected` handler it asserts `args.Peer.HasStarted == false` (an internal seam). On the unfixed
code `peer.Start()` ran inside the lock first, so `HasStarted` is `true` there → the test fails (verified
by reverting). `BugFixRegressionTests.RpcHost_RaisesPeerConnectedBeforePeerDisconnected…` remains as a
behavioural smoke test.

---

## #5 — `NamedPipeClientTransport`: connection leak if `DisposeAsync` races `ConnectAsync`

**Location:** `src/ShaRPC.Transports.NamedPipes/NamedPipeClientTransport.cs`, `ConnectAsync`.

**Defect.** If `DisposeAsync` runs after `stream.ConnectAsync(ct)` succeeds but before/while
`_connection` is assigned, `DisposeAsync` observes `_connection == null` and disposes only `_stream`
(or nothing). `ConnectAsync` then publishes a live `StreamConnection` (which owns the stream) into an
already-disposed transport, leaking it past disposal.

**Fix.** After publishing `_connection`, re-check the disposed flag and tear down — mirroring the
identical, already-present guard in `TcpTransport.ConnectAsync`:

```diff
             stream.Dispose();
             throw;
         }
+
+        if (Volatile.Read(ref _disposed) != 0)
+        {
+            await _connection.DisposeAsync().ConfigureAwait(false);
+            throw new ObjectDisposedException(nameof(NamedPipeClientTransport));
+        }
     }
```

**Hardening.** A full `Interlocked.MemoryBarrier()` was added between the `_connection` publication and
the disposed re-check (and the same in `TcpTransport.ConnectAsync`). Without it the re-check is only an
acquire `Volatile.Read`, so an x86/x64 store-buffer (Dekker) interleaving could let `ConnectAsync` miss
the disposed flag while `DisposeAsync` misses `_connection`, reproducing the leak. The barrier makes the
publication globally visible before `_disposed` is read, completing the Dekker pattern (the dispose side
already fences via `Interlocked.Exchange`).

**Proven by:** `RaceConditionDeterministicTests.NamedPipeClientTransport_DisposedDuringConnect_TearsDownAndThrows`
uses an internal connect-published seam to run `DisposeAsync` exactly at the publication point, then
asserts `ConnectAsync` throws `ObjectDisposedException` and `IsConnected == false`. On the unfixed code
(no re-check) `ConnectAsync` returns normally → the test fails (verified by reverting). The earlier entry-
guard test remains as a smoke test.

---

## #6 — `TcpServerTransport`: stashed `_pendingAccept` could be double-consumed

**Location:** `src/ShaRPC.Transports.Tcp/TcpServerTransport.cs`, `AcceptAsync` + `ObservePendingAccept`.

**Defect.** When an `AcceptAsync` is cancelled it stashes the in-flight `Task<TcpClient>` in
`_pendingAccept` to hand back on the next call. The next `AcceptAsync` consumed it with a **non-atomic**
`var t = _pendingAccept ?? listener.AcceptTcpClientAsync(); _pendingAccept = null;`, while
`ObservePendingAccept` (called from `StopAsync`/`DisposeAsync`, on any thread) does
`Interlocked.Exchange(ref _pendingAccept, null)`. The two could each take the *same* stashed task:
`AcceptAsync` returns the resulting `TcpClient` to its caller while `ObservePendingAccept` also disposes
it — handing back a dead/disposed connection.

**Fix.** Consume atomically with `Interlocked.Exchange`, and re-stash atomically with a reclaim-if-stopped
guard:

```diff
-        var acceptTask = _pendingAccept ?? listener.AcceptTcpClientAsync();
-        _pendingAccept = null;
+        var acceptTask = Interlocked.Exchange(ref _pendingAccept, null) ?? listener.AcceptTcpClientAsync();
         ...
-                _pendingAccept = acceptTask;
+                _ = Interlocked.Exchange(ref _pendingAccept, acceptTask);
+                if (Volatile.Read(ref _started) == 0 || Volatile.Read(ref _disposed) != 0)
+                {
+                    ObservePendingAccept();
+                }
                 throw new OperationCanceledException(ct);
```

**Proven by:** `RaceConditionDeterministicTests.TcpServerTransport_PendingAcceptConsume_LosesRaceToConcurrentReclaim`.
The consume was factored into an internal `ClaimPendingAccept()` (read → seam → `CompareExchange`). The
test stashes a task, and from the seam runs a competing `Interlocked.Exchange` (simulating shutdown's
`ObservePendingAccept`); it then asserts the claim **lost** the race and returned `null`. On the unfixed
non-atomic consume the claim returns the stashed task anyway (double-take) → the test fails (verified by
reverting). The functional stash/reuse regression test remains as a smoke test.

---

## #8/#9 — `StreamConnection`: removed unreachable `totalLength == 4` branch

**Location:** `src/ShaRPC.Core/Transport/StreamConnection.cs`, `ReceiveAsync`.

**Not a behavior bug — dead-code cleanup.** `ValidateIncomingLength(totalLength)` runs first and throws
`InvalidDataException` for any `totalLength < MessageFramer.HeaderSize` (= 9). The subsequent
`if (totalLength == 4) return frame;` is therefore unreachable; if it *were* reached it would return a
structurally malformed (header-only-shaped) frame. Removing it is behavior-preserving and removes a
future-maintenance hazard (e.g. if `HeaderSize` changed or the branch were moved above the validation).

```diff
             var frame = Payload.Rent(totalLength);
             BinaryPrimitives.WriteInt32LittleEndian(frame.Memory.Span.Slice(0, 4), totalLength);
-
-            if (totalLength == 4)
-            {
-                return frame;
-            }
```

**Current tests:** `StreamConnectionCoverageTests.ReceiveAsync_Throws_WhenTotalLengthEqualsFour` (proves
`totalLength == 4` throws at validation, i.e. the branch was unreachable) and
`BugFixRegressionTests.StreamConnection_HeaderOnlyFrame_RoundTripsAfterDeadBranchRemoval` (the adjacent
minimal-valid-frame boundary still works). There is nothing to red-test because behavior did not change.

---

## #10 — `ShaRpcGeneratedAssemblyCatalog`: fault-recovery could evict a successor entry

**Location:** `src/ShaRPC.Core/Generated/ShaRpcGeneratedAssemblyCatalog.cs`, `EnsureRegistered`.

**Defect.** On a faulted registration the catch did `s_registrationAttempts.TryRemove(assembly, out _)`
— a **key-only** removal. If another thread had already replaced the faulted `Lazy<bool>` with a fresh
successor under the same key, this removed the *successor*, discarding a registration attempt that was
in progress or had succeeded. (Independent review note: this is recoverable — a later
`EnsureRegistered`/`GetOrAdd` re-creates the entry — so it is a wrongly-discarded successor, not a
*permanent* deregistration as an earlier draft of this doc claimed.)

**Fix.** Value-comparing removal that only evicts the faulted `Lazy` this call actually holds:

```diff
-            s_registrationAttempts.TryRemove(assembly, out _);
+            ((ICollection<KeyValuePair<Assembly, Lazy<bool>>>)s_registrationAttempts)
+                .Remove(new KeyValuePair<Assembly, Lazy<bool>>(assembly, registration));
```

**Proven by:** `RaceConditionDeterministicTests.ShaRpcGeneratedAssemblyCatalog_FaultRecovery_PreservesSuccessor`.
The eviction was factored into an internal `EvictFaultedAttempt(assembly, faulted)`. Using a unique
throwaway `AssemblyBuilder` assembly (so it never collides with a real catalog entry), the test installs a
faulted attempt, replaces it with a successor `Lazy`, then runs `EvictFaultedAttempt` holding only the
faulted one — and asserts the successor survives. On the unfixed key-only `TryRemove` the successor is
evicted → the test fails (verified by reverting). `ConcurrentDictionary`'s
`ICollection<KeyValuePair>.Remove` does reference-equality on the value, which is what makes the fix sound.

---

## Independent verification

Because these rest on reasoning rather than a red→green test, each was re-checked by **15 independent
subagents** — three adversarial lenses per fix (is the defect *real*? / is the fix *correct & free of
new races*? / is a *deterministic test* actually possible?), each reasoning from the raw `git diff` and
surrounding code without access to the conclusions above.

| Fix | Defect real? | Fix correct? | Deterministic test possible? |
|-----|--------------|--------------|------------------------------|
| #2 RpcHost event ordering | ✅ yes (3/3) | ✅ yes (3/3) — no new unstarted/started-after-shutdown window | ✅ yes, with a seam |
| #5 NamedPipe client guard | ✅ yes (3/3) | ⚠️ **correct but incomplete** | ✅ yes, with a seam |
| #6 TcpServer `_pendingAccept` | ✅ yes (3/3) | ✅ yes (3/3) | ✅ yes, with a seam |
| #8/9 StreamConnection dead branch | ✅ confirmed **no** behavior bug | ✅ strictly behavior-preserving | n/a |
| #10 catalog fault-recovery | ✅ yes (3/3) | ✅ yes (3/3) | ✅ yes, with a seam |

**All five fixes were independently confirmed correct.** The review added three refinements:

1. **#5 has a residual weak-memory-model hole (not newly introduced).** The disposed re-check is a
   `Volatile.Read` (acquire), not a full store-load fence, so a narrow store-buffer/Dekker interleaving
   can still let `ConnectAsync` miss the disposed flag while `DisposeAsync` misses the published
   connection — reproducing the original leak. This is **identical to the mirrored `TcpTransport.ConnectAsync`**,
   so it is a pre-existing parity gap rather than a regression. An `Interlocked.MemoryBarrier()` between
   the `_connection` publication and the disposed re-check (in *both* transports) would close it provably.

2. **#10 framing corrected** (see the note in that section): the mis-eviction is recoverable on retry,
   not a permanent deregistration.

3. **The four races ARE deterministically testable with a small library testability seam** — e.g. an
   internal hook invoked at the top of the read loop (#2: assert the loop has not started when
   `PeerConnected` fires), or between the `_connection` publication and the disposed re-check (#5), or an
   injectable barrier around `_pendingAccept` consume (#6). The current tests are guards only because no
   such seam exists in the library yet; adding one would convert each into a true red→green test.

No lens found a regression, deadlock, or new race introduced by any of the five fixes.
