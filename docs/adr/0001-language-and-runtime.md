# ADR 0001 — Language and runtime

**Status:** Accepted
**Date:** 2026-11-11

## Context

Delily is a from-scratch mail server. The repository already targets
`net10.0` with the ASP.NET Core web template, and the installed SDK is
11.0.100. The implementation needs:

- raw TCP servers with precise byte framing (SMTP/IMAP);
- TLS via a well-maintained stack;
- cryptography (RSA, Ed25519, SHA-256, Argon2id, base64);
- a durable database and background workers;
- HTTP for JMAP/admin/health;
- strong tooling, observability, and test libraries.

## Decision

Use **C# on .NET 10** (`net10.0`) as the implementation platform.

- Host: ASP.NET Core generic host + minimal APIs for HTTP.
- Protocol listeners: `System.Net.Sockets` + `System.IO.Pipelines`.
- TLS: `SslStream` with the OS trust store.
- Crypto: `System.Security.Cryptography`; BouncyCastle only for a missing
  primitive.
- Data: PostgreSQL via Npgsql/EF Core; Dapper for hot paths if needed.
- Tests: xUnit, FsCheck/CsCheck, Testcontainers, SharpFuzz.
- Observability: OpenTelemetry + Serilog.

## Consequences

Positive:

- One language for protocol servers, workers, and HTTP.
- Excellent async I/O, TLS, and crypto primitives.
- Mature tooling and a large test ecosystem.
- Easy deployment as a container or self-contained binary.

Negative:

- GC pauses and allocation pressure need attention in hot parsers.
- Some mail-specific libraries are less mature than C/Python equivalents.
- `SslStream` gives less control than OpenSSL for exotic TLS policies.

Mitigations:

- Use `ReadOnlySequence<byte>`/`Span<byte>` in parsers; avoid per-line strings.
- Benchmark with BenchmarkDotNet and profile allocation.
- Keep protocol code free of LINQ in hot paths.
- Isolate any native dependency behind an interface.

## Alternatives considered

- **Rust/Go:** excellent for network servers, but the repository is .NET and
  the team’s velocity is higher in C#.
- **C/C++:** maximum control, too slow to build safely.
- **Node/TypeScript:** fast iteration, weaker CPU-bound parsing and crypto
  ergonomics.
