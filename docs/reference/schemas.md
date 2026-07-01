# Schemas reference

Kernel and plugin payloads are validated against versioned JSON Schemas, which are also embedded in the
relevant NuGet packages and regression-tested for drift against the importer.

These schemas are published first-class because the source generators and attributes are opt-in sugar over
these public primitives, never lock-in ([design guidelines](https://github.com/JKamsker/DotBoxD/blob/main/rules/design-guidelines.md)):
you can hand-author or validate the same IR module or plugin manifest against the schema without any
generator, so reach for them when you need to emit, inspect, or verify a payload by hand.

| Schema | File | Accepted by |
|--------|------|-------------|
| Kernel module envelope | [`schemas/v1/dotboxd-kernel-module.schema.json`](https://github.com/JKamsker/DotBoxD/blob/main/schemas/v1/dotboxd-kernel-module.schema.json) | the JSON IR importer in `DotBoxD.Kernels.Serialization.Json` |
| Plugin package envelope | [`schemas/v1/dotboxd-plugin-package.schema.json`](https://github.com/JKamsker/DotBoxD/blob/main/schemas/v1/dotboxd-plugin-package.schema.json) | the plugin-package importer in `DotBoxD.Plugins` |

The schemas are a versioned contract (the `v1/` directory). A regression test keeps each schema in sync
with the code that consumes it, so a schema change that diverges from the importer fails CI.

## See also

- [Kernels](../concepts/kernels.md)
- [Pushdown Step 6 — hand-authoring the payload](../tutorials/pushdown-server-extension.md#step-6--diagnostics-and-the-no-lock-in-principle)
- [Glossary](glossary.md)
