# Example JSON IR

## Pure calculation

```json
{
  "id": "loot-score",
  "version": "1.0.0",
  "targetSandboxVersion": "1.0.0",
  "capabilityRequests": [],
  "functions": [
    {
      "id": "main",
      "visibility": "entrypoint",
      "parameters": [
        { "name": "level", "type": "I32" },
        { "name": "rarity", "type": "I32" }
      ],
      "returnType": "I32",
      "body": [
        {
          "op": "set",
          "name": "base",
          "value": { "op": "mul", "left": { "var": "level" }, "right": { "i32": 10 } }
        },
        {
          "op": "set",
          "name": "bonus",
          "value": { "op": "mul", "left": { "var": "rarity" }, "right": { "i32": 25 } }
        },
        {
          "op": "return",
          "value": { "op": "add", "left": { "var": "base" }, "right": { "var": "bonus" } }
        }
      ]
    }
  ]
}
```

Effects:

```text
Cpu
```

## File read through safe binding

```json
{
  "id": "config-reader",
  "version": "1.0.0",
  "targetSandboxVersion": "1.0.0",
  "capabilityRequests": [
    { "id": "file.read", "reason": "Read tenant-local config" }
  ],
  "functions": [
    {
      "id": "main",
      "visibility": "entrypoint",
      "parameters": [],
      "returnType": "String",
      "body": [
        {
          "op": "return",
          "value": {
            "call": "file.readText",
            "args": [{ "path": "config/settings.json" }]
          }
        }
      ]
    }
  ]
}
```

Inferred effects:

```text
Cpu | Alloc | FileRead | Concurrency
```

Required grant:

```json
[
  {
    "id": "file.read",
    "parameters": {
      "root": "C:\\tenant\\123\\config",
      "maxBytesPerRun": 262144
    }
  },
  {
    "id": "dotboxd.runtime.async",
    "parameters": {}
  }
]
```

Lowering target:

```text
file.readText(path)
  -> binding slot 3
  -> SafeFileSystem.ReadText(ctx, path)
```

The IR never references `System.IO.File`.

## Interpreted execution

```text
json IR -> import -> validate -> effects -> policy -> direct IR interpreter
```

No IL, `DynamicMethod`, or DLL is generated.

## Compiled execution

```text
json IR -> import -> validate -> effects -> policy -> compiled runtime artifact -> verifier/gate -> run
```

## Forbidden examples

```json
{ "call": "System.IO.File.ReadAllText", "args": [{ "string": "secret.txt" }] }
```

```json
{ "call": "Type.GetType", "args": [{ "string": "System.IO.File" }] }
```

Both are invalid. Binding IDs are statically resolved from trusted host descriptors.
