# ADR 0002 — Modular monolith with protocol/policy/storage boundaries

**Status:** Accepted
**Date:** 2026-11-11

## Context

A mail server has several protocols and a shared message store. Splitting
into microservices too early creates distributed transactions, duplicate
models, and operational overhead before the domain is understood. A single
process with no boundaries becomes an untestable tangle.

## Decision

Build a **modular monolith** with explicit projects/assemblies:

```
Delily.Host
Delily.Protocols.Smtp
Delily.Protocols.Imap
Delily.Protocols.Jmap
Delily.Mime
Delily.Auth
Delily.Transport
Delily.Queue
Delily.Storage
Delily.Delivery
Delily.Abuse
Delily.Observability
```

Rules:

- Protocol projects depend on abstractions (interfaces), not on `Storage`
  directly.
- `Storage` owns persistence; no SQL outside it.
- `Auth` is pure policy over parsed messages and DNS results.
- `Queue`/`Delivery` own retry/DSN semantics.
- `Host` wires listeners and workers; it contains no protocol logic.
- One process can host all listeners; each can be disabled by config.

## Consequences

Positive:

- Testable units with clear seams.
- One deployment, one transaction boundary for accept.
- Easy to split later if a component needs independent scaling.

Negative:

- A change can cross several projects; keep interfaces small.
- Temptation to reference `Storage` from `Protocols`; enforce with
  architecture tests.

Mitigations:

- Add an architecture test (NetArchTest/ArchUnitNET) that forbids forbidden
  references.
- Keep DTOs in a shared `Contracts` project only when necessary.

## Alternatives considered

- **Single project:** fastest start, hardest to keep clean.
- **Microservices:** premature; distributed queue/DB complexity.
- **Actor model:** interesting, but not needed for the first milestones.
