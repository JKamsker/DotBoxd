# ADR 0001 — Use Restricted IR, Not Arbitrary C#

## Status

Accepted.

## Context

The project wants in-process plugin/user logic with sandboxed API access. Running arbitrary C# in-process is not a safe default because C# and .NET expose many escape hatches, including reflection, dynamic invocation, delegates, expression compilation, assembly loading, native interop, threading, process APIs, environment APIs, and raw object access.

API blacklists are fragile. Reference allowlists are better but still not sufficient if arbitrary language/runtime features can be used to reach power indirectly.

## Decision

Users will not write arbitrary C#.

Users submit a restricted JSON IR document. The IR cannot express arbitrary CLR calls or raw MSIL. Every side-effecting operation is represented as a known sandbox operation and requires host-granted capability.

## Consequences

Positive:

- smaller attack surface
- easier validation
- effect analysis possible
- interpreter possible
- generated code verifier possible
- policy can be reasoned about before execution

Negative:

- less expressive than C#
- requires JSON IR tooling work
- host must design safe APIs intentionally
- users need to learn the JSON IR shape

## Rejected alternative

Compile user C# with limited references and scan for bad APIs.

Rejected because it relies on absence of mistakes in a huge language/runtime surface and is not a solid sandbox boundary.
