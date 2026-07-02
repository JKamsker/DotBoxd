---
title: 'Diagnostics reference'
description: 'DotBoxD''s compile-time generators/analyzers and runtime emit namespaced diagnostic codes. Each family has a reserved prefix so codes never collide as the…'
---
DotBoxD's compile-time generators/analyzers and runtime emit namespaced diagnostic codes. Each family
has a reserved prefix so codes never collide as the product grows.

These diagnostics exist because the analyzer and kernel validators fail **closed**: an unsupported
construct is rejected at build time (or at plugin import time) instead of being silently miscompiled or
lowered into something that misbehaves at runtime. So a `DBXS`/`DBXK` code means "this construct isn't
supported here" — it's telling you to express the intent a different way, not a bug in the generator to
work around.

| Prefix | Area | Source |
|--------|------|--------|
| `DBXS` | **Services** — `[DotBoxDService]` proxy/dispatcher generation | `DotBoxD.Services.SourceGenerator` |
| `DBXK` | **Kernels / plugins** — plugin authoring + validation | `DotBoxD.Plugins.Analyzer` + kernel validators |
| `DBXP` | **Pushdown** | reserved |
| `DBXH` | **Hosting** | reserved |
| `DBXT` | **Transports** | reserved |
| `DBXG` | **Generators / codegen (shared)** | reserved |

## Shipped Services codes (`DBXS###`)

If you hit one of these while generating a `[DotBoxDService]` proxy, look it up here:

| ID | Severity | Meaning |
|--------|----------|---------|
| `DBXS001` | Error | DotBoxD source generator failure |
| `DBXS002` | Error | Unsupported method shape (e.g. a `ref`/`in`/`out` parameter) |
| `DBXS003` | Error | Unsupported service shape (e.g. a generic or nested interface) |
| `DBXS004` | Warning | Async sibling interface method name collides with another method |

For `DBXS002`/`DBXS003`, adjust the contract (drop the `ref`/`in`/`out` parameter, or lift the interface
out to a non-nested, non-generic top-level type) and rebuild. See
[tutorials/first-service.md](/tutorials/first-service/) for a worked example.

## Authoritative lists

The shipped/unshipped code lists are maintained alongside each generator and are CI-enforced. These are
the source of truth — including the full `DBXK###` set, which is not reproduced here:

- Services (`DBXS###`): [`AnalyzerReleases.Shipped.md`](https://github.com/JKamsker/DotBoxD/blob/main/src/CodeGeneration/DotBoxD.Services.SourceGenerator/AnalyzerReleases.Shipped.md)
  and its [`AnalyzerReleases.Unshipped.md`](https://github.com/JKamsker/DotBoxD/blob/main/src/CodeGeneration/DotBoxD.Services.SourceGenerator/AnalyzerReleases.Unshipped.md) sibling.
- Kernels/plugins (`DBXK###`): [`AnalyzerReleases.Shipped.md`](https://github.com/JKamsker/DotBoxD/blob/main/src/CodeGeneration/DotBoxD.Plugins.Analyzer/AnalyzerReleases.Shipped.md)
  (and the kernel runtime diagnostic-code source).

> Migration note: these were renamed during the merge — ShaRPC's `SHARPC###` → `DBXS###` and Safe-IR's
> `SGP###` → `DBXK###`. If you previously suppressed any old IDs, update your `.editorconfig` /
> `<NoWarn>`. See [migration-from-standalone-repos.md](/contributing/migration-from-standalone-repos/).
