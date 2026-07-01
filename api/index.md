# API reference

This reference is generated directly from the source of every published DotBoxD package, so it always
matches the code on `main`. Pick a namespace from the sidebar, or start with the entry points below.

## Where to start

| You want to… | Reach for |
|--------------|-----------|
| Define and host an RPC contract | <xref:DotBoxD.Services?text=DotBoxD.Services> (`[DotBoxDService]`, `RpcPeer`, `RpcHost`) |
| Import, validate, and run a kernel under policy | <xref:DotBoxD.Hosting?text=DotBoxD.Hosting> (`SandboxHost`, `SandboxPolicyBuilder`) |
| Work with the IR model, values, and resource limits | <xref:DotBoxD.Kernels?text=DotBoxD.Kernels> (`SandboxValue`, `SandboxType`, `ResourceLimits`) |
| Round-trip kernels as JSON IR | <xref:DotBoxD.Kernels.Serialization.Json?text=DotBoxD.Kernels.Serialization.Json> (`JsonImporter`, `JsonExporter`) |
| Author plugins and server extensions | <xref:DotBoxD.Abstractions?text=DotBoxD.Abstractions> (`[Plugin]`, `[HostBinding]`, `[ServerExtension]`, `HookContext`) |
| Compose kernels next to host services over IPC | <xref:DotBoxD.Pushdown.Services?text=DotBoxD.Pushdown.Services> (`RpcMessagePackIpc`) |

## Conventions

- Source-generator diagnostics are namespaced `DBXS###` (Services) and `DBXK###` (Kernels / plugins).
  See [Reference › Diagnostics](../docs/reference/diagnostics.md).
- Public abstractions and generators are **opt-in sugar over public primitives** — anything a generator
  emits, you can hand-write against the same public API.

New here? The [Tutorials](../docs/tutorials/index.md) show these types working together end to end.
