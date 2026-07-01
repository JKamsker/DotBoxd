---
_disableToc: true
_disableAffix: true
---

# DotBoxD

**Source-generated, contract-first .NET extension runtime.** One C# contract, used three ways —
all driven by Roslyn source generators, with no runtime reflection on the hot path.

[![CI](https://github.com/JKamsker/DotBoxD/actions/workflows/ci.yml/badge.svg)](https://github.com/JKamsker/DotBoxD/actions/workflows/ci.yml)
[![NuGet packages](https://img.shields.io/badge/NuGet-packages-004880.svg?logo=nuget)](https://www.nuget.org/packages?q=DotBoxD)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/JKamsker/DotBoxD/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-512BD4.svg)](https://dotnet.microsoft.com/)

---

## The three ways to use one contract

| Mode | What it does | Start here |
|------|--------------|------------|
| **Services** | The host implements a contract; clients call it remotely over RPC. | [Tutorial ›](docs/tutorials/first-service.md) |
| **Kernels** | A client supplies validated logic the host runs safely inside a fuel-metered sandbox. | [Tutorial ›](docs/tutorials/first-kernel.md) |
| **Pushdown** | A plugin ships its *own* sandboxed batch operation that runs **server-side**, collapsing N round-trips into one. | [Tutorial ›](docs/tutorials/pushdown-server-extension.md) |

The Services and channel libraries target `netstandard2.1`, so they run on **Unity / IL2CPP**.
The Kernels and Pushdown stack targets `net10.0`.

## Install

```bash
# Full net10.0 stack (Services + Kernels + Pushdown):
dotnet add package DotBoxD --prerelease

# Unity / netstandard2.1 service bundle:
dotnet add package DotBoxD.Services.All --prerelease
```

Then follow [**Getting started**](docs/getting-started/README.md) or jump straight into a
[**Tutorial**](docs/tutorials/toc.yml).

## Explore the docs

- 📘 **[Guide](docs/toc.yml)** — concepts (Services, Kernels, Pushdown, the runtime), security model, and reference (diagnostics, schemas).
- 🎓 **[Tutorials](docs/tutorials/toc.yml)** — end-to-end walkthroughs for your first service, first kernel, and first pushdown extension.
- 🧩 **[Examples](docs/examples/toc.yml)** — an annotated tour of the maintained GameServer sample.
- 🔎 **[API reference](api/toc.yml)** — generated from the source of every published package.

## Security in one line

**Safe mode is the real boundary:** a kernel is restricted IR that is validated, capability-gated,
fuel/quota-metered, and (for compiled mode) verified before it runs — consumers never supply C#, IL,
or arbitrary host calls. Trusted-plugin mode (`AssemblyLoadContext`) is **not** a sandbox. Read
[**Sandbox caveats**](docs/security/sandbox-caveats.md) before deploying.

---

DotBoxD merges the former standalone **ShaRPC** (RPC) and **Safe-IR** (kernel sandbox) projects into
one contract-first runtime. It is [MIT licensed](https://github.com/JKamsker/DotBoxD/blob/main/LICENSE).
