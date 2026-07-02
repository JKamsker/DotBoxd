---
name: library-surprise-sweep
description: Find and fix "library surprise" behavior where public APIs, attributes, generators, docs, or runtime contracts imply support but the implementation silently skips, lowers the wrong shape, misbehaves at runtime, or accepts unsupported input. Use when asked to harden a library, source generator, analyzer, RPC/marshalling layer, public abstraction, or plugin/runtime workflow by either adding first-class support or failing closed with explicit compiler diagnostics, analyzer errors/warnings, or validation errors.
---

# Library Surprise Sweep

## Goal

Make the library's public surface honest. If a reasonable user can infer that a shape is supported, either implement it correctly or reject it early with a clear diagnostic. Avoid silent skips, runtime-only failures, unfiltered installs, lossy marshalling, and generated code that compiles only for lucky cases.

## Workflow

1. Map the promise surface before editing.
   Read public APIs, attributes, docs, generator entry points, analyzer diagnostics, runtime validators, and nearby tests. Identify what a user would reasonably expect from names, overloads, type signatures, examples, and existing supported adjacent cases.

2. Build a surprise inventory.
   Look for shapes that are public or syntactically accepted but might not be honored:
   - named/default arguments, extension syntax, overloads, generated receivers, captures, async/cancellation, and object/collection initializers
   - DTO constructors, inherited members, accessibility, required/init members, computed fields, maps/lists/enumerables, enums, nullable and temporal types
   - generated member collisions, keyword escaping, setup replay, retry semantics, orphan installs, hook-chain filters, local/remote fallback paths
   - analyzer gaps caused by syntax-only checks, identifier-text matching, missing Roslyn operation kinds, initializer contexts, method groups, accessors, or generated code

3. Prove each surprise with a focused failing test first.
   Prefer the repo's existing regression-test style. Assert the user-visible behavior: generated diagnostics, generated source/IR shape, runtime round trip, install result, or exact failure mode. For source generators and analyzers, include tests that exercise Roslyn symbols rather than only string matching.

4. Decide support versus fail-closed.
   Add first-class support when the behavior can be lowered to public primitives while preserving user semantics. Preserve evaluation order, single-evaluation of side effects, accessibility rules, cancellation, exact data shape, and retry/cleanup behavior.

   If support would be ambiguous, unsafe, lossy, or lock users into non-public implementation details, reject it at compile time or generation time. Emit the repo's established diagnostic ID/severity where one exists, with a message that names the unsupported shape and the actionable reason.

5. Implement narrowly.
   Use existing analyzers, lowerers, binders, validators, and runtime mappers instead of creating parallel logic. Prefer Roslyn symbols/operations and structured runtime type inspection over syntax text or string heuristics. Keep changes scoped, maintainable, and consistent with the codebase's design rule: public abstractions and generators must be opt-in sugar over public primitives, never lock-in.

6. Guard the fix.
   Add regression coverage for both the happy path and the rejection path when applicable. If the repo has required-test gates, add the new regression class or group with an accurate minimum count and update CI wiring. Run the relevant local build, format, tests, file/folder gates, and any project-specific required-test scripts before handoff.

## Acceptance Criteria

- No public-looking shape is left silently skipped or mis-lowered.
- Supported shapes round-trip or lower exactly as a user would expect.
- Unsupported shapes fail closed before runtime with explicit diagnostics or validation errors.
- Tests fail without the implementation change and pass with it.
- CI-required regression gates protect the newly discovered behavior.
