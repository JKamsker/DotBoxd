# ADR 0003 — Host-Granted Bindings Only

## Status

Accepted.

## Context

The user wants scripts to call some .NET/host APIs safely. A dangerous interpretation would let script authors bind arbitrary .NET APIs themselves.

If untrusted code can grant or define CLR bindings, it can grant itself escape routes.

## Decision

Only trusted host/server owner code may register bindings and grant capabilities.

Script authors may request capabilities, but requests are not grants.

## Consequences

Positive:

- central review of exposed APIs
- capability policy remains authoritative
- binding manifests can be hashed/versioned
- compiled cache can include binding manifest hash

Negative:

- script authors cannot freely integrate arbitrary libraries
- host must maintain binding catalog

## Example

Allowed:

```text
JSON IR: capabilityRequests contains file.read
Host policy: grants file.read root=tenant-config
Host binding: file.readText -> SafeFileSystem.ReadText
```

Forbidden:

```text
Script: bind read = System.IO.File.ReadAllText
```
