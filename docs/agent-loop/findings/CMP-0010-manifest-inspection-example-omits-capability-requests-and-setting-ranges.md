---
id: CMP-0010
area: completeness
status: open
priority: medium
title: Manifest inspection example omits capability requests and setting ranges
dedup_key: cmp/plugin-admin-review/manifest-inspection-omits-capabilities-and-ranges
created_at: 2026-06-12T22:08:14.8787385+00:00
created_by: continuous-completeness-producer
created_commit: 
updated_at: 2026-06-12T22:08:14.8787385+00:00
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

# CMP-0010: Manifest inspection example omits capability requests and setting ranges

## Claim

The runnable admin/manifest inspection example omits fields that the docs say server owners must review before enabling a plugin. It prints live setting name/type/default, effects, and subscriptions, but not capability requests or live setting range bounds.

## Why this matters

The manifest review surface is the operator's pre-install safety gate for plugin permissions and settings. If the maintained example leaves out requested capabilities and numeric constraints, admin UI authors can copy an incomplete review model and miss the permission or bounds data that should drive approval.

## Evidence

- `docs/Specs/Addendum/Examples.md:107` says the runtime enforces supported live setting types and numeric ranges during install and live updates.
- `docs/Specs/Addendum/Examples.md:205` introduces `Inspect Plugin Permissions` as the admin review walkthrough.
- `docs/Specs/Addendum/Examples.md:230` through `docs/Specs/Addendum/Examples.md:234` list the sample capability request `game.message.write` and subscription data as part of the package review output.
- `docs/Specs/Addendum/Examples.md:237` says server owners need to show settings, defaults, ranges, requested effects, and hook subscriptions before install.
- `examples/Addendum/DotBoxd.Kernels.AddendumExamples/Examples/ManifestInspectionExample.cs:10` prints live setting name/type/default only.
- `examples/Addendum/DotBoxd.Kernels.AddendumExamples/Examples/ManifestInspectionExample.cs:14` prints effects.
- `examples/Addendum/DotBoxd.Kernels.AddendumExamples/Examples/ManifestInspectionExample.cs:18` prints subscriptions.
- The example contains no loop/output for manifest capability requests and no min/max or range output for live settings.

## Suggested acceptance test

Add an example smoke test or captured-output assertion for `ManifestInspectionExample.Run()` that requires the output to include the `game.message.write` capability request, each requested effect, each subscription, and the `MinDamage` live setting range bounds from the generated manifest.

## Suggested fix direction

Update the manifest inspection example and addendum docs to print the full review surface: plugin identity, live setting defaults and constraints, capability requests with reasons, requested effects, hook subscriptions, and any execution-mode/admin status fields intended for pre-install review.

## Scope boundaries

Do not change plugin validation behavior in this finding. Keep the fix to docs/examples and smoke coverage for the admin review surface.

## Deduplication key

`cmp/plugin-admin-review/manifest-inspection-omits-capabilities-and-ranges`
