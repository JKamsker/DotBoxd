# Migration: from ShaRPC + Safe-IR to DotBoxD

DotBoxD is the merger of two formerly standalone repositories into one contract-first runtime:

| Former repo | Became | DotBoxD role |
|-------------|--------|--------------|
| **ShaRPC** (transport-agnostic RPC framework) | `DotBoxD.Services`, `DotBoxD.Transports.*`, `DotBoxD.Codecs.MessagePack`, `DotBoxD.Services.SourceGenerator` | **Services** + Channels |
| **Safe-IR** (restricted-IR kernel sandbox) | `DotBoxD.Kernels.*`, `DotBoxD.Hosting`, `DotBoxD.Abstractions`, `DotBoxD.Plugins`, `DotBoxD.Plugins.Analyzer`, `DotBoxD.Pushdown.Services`, `DotBoxD.Hosting.Http` | **Kernels** + **Pushdown** |

Both projects were MIT-licensed; both copyright notices are preserved in [`LICENSE`](https://github.com/JKamsker/DotBoxD/blob/main/LICENSE).
The original root files of ShaRPC (README, solution, license) are archived under
[`docs/legacy/`](https://github.com/JKamsker/DotBoxD/tree/main/docs/legacy).

## What changed in the merge

- **Namespaces/packages** were renamed `ShaRPC.*` / `SafeIR.*` → `DotBoxD.*` (see the package table in
  the root [README](https://github.com/JKamsker/DotBoxD/blob/main/README.md)).
- **Diagnostic IDs** were renamed: ShaRPC's `SHARPC###` → `DBXS###` (Services); Safe-IR's `SGP###` →
  `DBXK###` (Kernels). If you suppressed any of the old IDs, update your suppressions.
- **Marker attributes**: `[ShaRpcService]`/`[ShaRpcMethod]` → `[DotBoxDService]`/`[DotBoxDMethod]`.
- **JSON schemas** were renamed to `schemas/v1/dotboxd-kernel-module.schema.json` and
  `dotboxd-plugin-package.schema.json`.
- **Build**: one solution (`DotBoxD.slnx`), Central Package Management (`Directory.Packages.props`),
  and the former NuGet dependency Safe-IR took on ShaRPC is now an in-repo `ProjectReference`.

## Viewing the pre-merge git history

Both repositories' **full histories are preserved and reachable** in this repo — they were imported via
a git *subtree merge*, so every original commit (and its author) is an ancestor of the merge commits
`merge: import ShaRPC history ...` and `merge: import Safe-IR history ...`.

Because subtree-merge introduces the files under a new path at the merge boundary, `git log --follow`
from a file's **current** path stops at the import. To see a file's pre-merge history, query by its
**original** path against all history:

```bash
# Pre-merge history of a former ShaRPC core file:
git log --all -- src/ShaRPC.Core/RpcPeer.cs

# Pre-merge history of a former Safe-IR core file:
git log --all -- src/SafeIR.Core/SandboxModule.cs

# Or browse from the import-merge's second parent:
git log <import-merge-sha>^2
```

`git blame` on the current files works up to the merge; for older lines, blame the original path on the
imported history (`git blame <import-merge-sha>^2 -- <original-path>`).

## Building & testing

```bash
dotnet build DotBoxD.slnx -c Release
dotnet test  DotBoxD.slnx -c Release
```

See [`CONTRIBUTING.md`](../../CONTRIBUTING.md) for the full CI gate list (security-boundary suite,
API baselines, file-length, spec manifest, rebrand-completeness, docs smoke).
