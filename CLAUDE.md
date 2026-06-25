# CLAUDE.md

Guidance for Claude Code / AI agents working in this repository.

- **Repository expectations, C# size guard, and validation:** see [`AGENTS.md`](AGENTS.md).
- **Binding design rules:** see [`rules/design-guidelines.md`](rules/design-guidelines.md). The
  primary rule — **public abstractions and source generators are opt-in sugar over public
  primitives, never lock-in.** A consumer must always be able to hand-write the same thing with
  public API (*"can you delete the attribute and hand-write it?"*). Honor this on every new helper,
  builder, or generator.
- **Human contributor guide:** see [`CONTRIBUTING.md`](CONTRIBUTING.md).
