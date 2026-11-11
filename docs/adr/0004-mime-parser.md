# ADR 0004 — MIME parser: MimeKit behind a narrow interface

**Status:** Accepted
**Date:** 2026-11-11

## Context

MIME parsing is a security boundary. Malformed multipart, encoded words,
parameter continuations, charsets, nested messages, and huge attachments are
common. Writing a parser from scratch is a multi-month project and a likely
source of crashes, infinite loops, and header injection.

The project goal is to learn mail *protocols and delivery*, not to re-create a
battle-tested MIME parser.

## Decision

Use **MimeKit** for MIME parsing/writing, hidden behind `IMimeParser`:

```csharp
public interface IMimeParser
{
    ParseResult Parse(ReadOnlySequence<byte> raw, ParseOptions options);
}

public sealed record ParseResult(
    MimeMessage Message,
    IReadOnlyList<ParseDefect> Defects,
    string RawSha256);
```

Rules:

- Store the raw bytes; MimeKit is a *view*, not the source of truth.
- Enforce our own limits before/while parsing: header size, depth, part
  count, decoded size, archive depth.
- Never let MimeKit resolve external entities or fetch URLs.
- Record defects instead of throwing away the message.
- Keep the parser version in the cache key for `BODYSTRUCTURE`/`ENVELOPE`.

## Consequences

Positive:

- Correct handling of real-world MIME, including obscure encodings.
- Security fixes upstream.
- Faster path to a usable server.

Negative:

- A dependency with its own bugs and API surface.
- Slightly less control over canonicalization; verify DKIM on raw bytes, not
  on MimeKit’s re-serialization.
- Licensing/attribution obligations (MIT; keep the notice).

Mitigations:

- Wrap it; do not leak MimeKit types into protocol/storage layers.
- Fuzz our facade with a corpus.
- Add a golden corpus for regressions.
- If MimeKit is ever replaced, the interface keeps the blast radius small.

## Alternatives considered

- **From scratch:** valuable for learning, too risky for the first release.
- **`System.Net.Mail`:** not a robust MIME parser for arbitrary mail.
- **OpenPop/other abandoned libraries:** maintenance risk.
