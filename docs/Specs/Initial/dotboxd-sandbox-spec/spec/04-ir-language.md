# 04 — JSON IR

## IR design goals

The user-facing format is JSON IR. There is no custom text DSL, lexer, or parser.

The JSON IR must be:

- closed: no arbitrary external references
- typed: every value has a known sandbox type
- effect-aware: every side-effecting operation is visible
- deterministic to canonicalize/hash
- unambiguous: duplicate object properties are rejected
- named with non-empty identifiers and call names that contain no control characters
- easy to deserialize with normal JSON tooling
- easy to interpret
- easy to lower to a safe compiled runtime form
- small enough to audit

## Module shape

```json
{
  "id": "loot-score",
  "version": "1.0.0",
  "targetSandboxVersion": "1.0.0",
  "capabilityRequests": [],
  "functions": [],
  "metadata": {}
}
```

No module field may contain CLR assembly-qualified type names, metadata tokens, method handles, or raw IL.

## Function shape

```json
{
  "id": "main",
  "visibility": "entrypoint",
  "parameters": [
    { "name": "level", "type": "I32" }
  ],
  "returnType": "I32",
  "body": []
}
```

Only `entrypoint` functions may be called by the host.

## Types

Primitive types may use strings:

```json
"I32"
```

Composite types must use JSON objects:

```json
{ "name": "List", "arguments": ["String"] }
{ "name": "Map", "arguments": ["String", "I32"] }
```

Generic type strings such as `"List<String>"` are rejected to avoid a separate type-expression parser.

## Statements

Supported statement shapes:

```json
{ "op": "set", "name": "base", "value": { "i32": 10 } }
{ "op": "return", "value": { "var": "base" } }
{ "op": "expr", "value": { "call": "log.info", "args": [{ "string": "hello" }] } }
```

Control flow:

```json
{
  "op": "if",
  "condition": { "op": "lt", "left": { "var": "level" }, "right": { "i32": 10 } },
  "then": [],
  "else": []
}
```

```json
{
  "op": "forRange",
  "local": "i",
  "start": { "i32": 0 },
  "end": { "var": "count" },
  "body": []
}
```

```json
{
  "op": "while",
  "condition": { "bool": true },
  "body": []
}
```

Loops are fuel-accounted. Recursion is disabled in the MVP.

## Expressions

Literals:

```json
{ "i32": 10 }
{ "i64": 10 }
{ "f64": 1.5 }
{ "bool": true }
{ "string": "text" }
{ "path": "config/settings.json" }
```

Variable reference:

```json
{ "var": "level" }
```

Binary operations:

```json
{ "op": "add", "left": { "var": "a" }, "right": { "var": "b" } }
```

Operation IDs:

```text
add sub mul div rem
eq ne lt lte gt gte
and or
```

`add`, `sub`, `mul`, `div`, `rem`, unary `-`, and ordering comparisons operate on numeric
values. Numeric operands must have the same sandbox type: `I32` with `I32`, `I64` with
`I64`, or `F64` with `F64`. The IR does not implicitly widen or narrow numeric values.
Arithmetic returns the operand type. Ordering comparisons return `Bool`.

Integer arithmetic is checked. Integer overflow, division by zero, and the minimum-value
divided or remaindered by `-1` overflow case fail the run with `InvalidInput`. `F64`
arithmetic accepts only finite input values and must fail with `InvalidInput` if the result
is `NaN`, positive infinity, or negative infinity.

`and` and `or` are short-circuiting boolean operators. Implementations should evaluate
the cheaper pure operand first when that can determine the result, even if that operand is
authored on the right side. Operands with effects outside CPU/allocation preserve authored
left-to-right order so file, network, log, clock, random, or other externally visible
operations are not silently dropped or reordered.

Unary operations:

```json
{ "unary": "not", "operand": { "var": "allowed" } }
{ "unary": "-", "operand": { "var": "amount" } }
```

Calls:

```json
{ "call": "file.readText", "args": [{ "path": "config/settings.json" }] }
```

Generic calls use `genericType`:

```json
{
  "call": "list.empty",
  "genericType": "String",
  "args": []
}
```

Map construction uses the full `Map<TKey,TValue>` type as `genericType`:

```json
{
  "call": "map.empty",
  "genericType": { "name": "Map", "arguments": ["String", "I32"] },
  "args": []
}
```

There is no generic `call typeName.methodName(args)` instruction.

## Capability requests

A module may request capabilities:

```json
{
  "capabilityRequests": [
    { "id": "file.read", "reason": "Load tenant-local config" }
  ]
}
```

Requests are not grants. Policy still controls whether a capability is available and with which parameters.

## Forbidden IR concepts

The JSON IR must not contain:

- raw CLR type names
- raw CLR method names except in trusted binding manifests
- assembly names supplied by users
- metadata tokens
- MSIL byte arrays
- pointer types
- `object`
- `dynamic`
- reflection handles
- delegates/function pointers
- unsafe memory instructions
- arbitrary exception types
- arbitrary attributes
- arbitrary generic type construction
- thread/task creation
- finalizers/destructors

## Host calls

A host call must resolve to a binding descriptor.

```json
{
  "op": "return",
  "value": {
    "call": "file.readText",
    "args": [{ "path": "config/settings.json" }]
  }
}
```

Binding descriptor:

```text
id: file.readText
parameters: [SandboxPath]
returns: String
effects: [FileRead]
capability: file.read
```

The JSON IR never references `System.IO.File`.

## Canonical serialization

Canonical IR serialization is produced from the imported IR model, not from the original JSON text.

Canonicalization requirements:

- UTF-8
- sorted maps
- stable IDs
- normalized invariant-culture numbers
- escaped canonical separators and control characters inside string fields
- no timestamps
- no machine-specific paths
- no dependency on JSON whitespace or property order

The canonical bytes are used for the IR hash.

## Minimal MVP operation set

MVP starts with:

- constants
- locals
- arithmetic
- comparisons
- `if`
- bounded `forRange`
- fuel-accounted `while`
- lists and maps through safe sandbox collection operations
- internal function calls without recursion
- host calls through bindings

Do not add advanced features until the verifier, interpreter, compiler, and tests are stable.
