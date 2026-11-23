# Architecture Decision Records

An ADR records one significant architectural decision: the context, what was decided, and what it
costs. ADRs are immutable once accepted. To change a decision, write a new ADR that supersedes the
old one, and set the old one's status to `Superseded by ADR-NNNN`.

## Index

| # | Title | Status | Date |
|---|---|---|---|
| [0001](0001-pure-csharp-simulation-core.md) | Pure C# simulation core | Accepted | 2026-11-19 |
| [0002](0002-deterministic-simulation.md) | Deterministic simulation | Accepted | 2026-11-19 |
| [0003](0003-struct-event-bus.md) | Struct event bus | Accepted | 2026-11-19 |
| [0004](0004-worldops-mutation-gateway.md) | WorldOps mutation gateway | Accepted | 2026-11-19 |
| [0005](0005-save-format-and-integrity.md) | Save format and integrity | Accepted (implemented; follow-ups open) | 2026-11-19 |
| [0006](0006-utility-ai-for-town-building.md) | Utility AI for town building | Accepted | 2026-11-19 |
| [0007](0007-housing-rule-3-to-5.md) | Housing rule: 3 to 5 residents per dwelling | Accepted | 2026-11-19 |
| [0008](0008-module-boundaries-and-blackboards.md) | Module boundaries and blackboards | Accepted | 2026-11-19 |

## Process

1. Copy the template below to `docs/adr/NNNN-short-kebab-title.md`, using the next free number.
2. Open a PR with status `Proposed`. Link it from the PR that needs the decision.
3. The lead accepts or rejects it in review. On merge, set the status and date and add a row to the
   index above.
4. A decision that changes `Core/Contracts`, the save format, determinism, or the system order
   **requires** an ADR.

## Template

```markdown
# ADR-NNNN: <Title>

- **Status:** Proposed | Accepted | Rejected | Deprecated | Superseded by ADR-NNNN
- **Date:** YYYY-MM-DD
- **Deciders:** <roles, e.g. lead, module owners>
- **Related:** <ADRs, contract sections, files>

## Context

What problem are we solving? Which forces and constraints apply (Unity compatibility, determinism,
performance, security, team structure)? State facts, not the solution.

## Decision

What we will do, stated in the active voice ("We will ..."). Include the concrete rules, types and
files that implement it.

## Consequences

### Positive
- ...

### Negative / costs
- ...

### Follow-ups
- ...

## Alternatives considered

### <Alternative A>
Why not.

### <Alternative B>
Why not.
```
