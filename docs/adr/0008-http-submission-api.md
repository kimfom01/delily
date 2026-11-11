# ADR 0008 — HTTP submission API: REST facade over the shared submission service

**Status:** Accepted
**Date:** 2026-11-11

## Context

Delily must accept mail from applications over HTTPS, not only from SMTP
clients. The same requirement could be met with:

1. **JMAP `EmailSubmission` only** — standards-based, but requires a full JMAP
   client model (blobs, Email objects, states) before a simple backend can
   send one message.
2. **A custom REST API** — simple for backends (`POST` JSON, get an ID), but
   not standardized.
3. **A direct SMTP relay** — applications speak SMTP themselves; many modern
   backends prefer HTTPS and cannot easily do STARTTLS/queueing.
4. **A separate delivery path for HTTP** — duplicates DKIM, queue, retry, and
   DSN logic; unacceptable.

## Decision

Implement a **Delily-specific REST submission API** plus a **raw MIME**
endpoint, both as thin adapters over the same submission service, DKIM signer,
and durable queue used by SMTP submission.

- Endpoints: `POST /api/v1/messages`, `POST /api/v1/messages/raw`,
  `GET /api/v1/messages/{id}`.
- Auth: tenant-scoped API keys (`dlly_...`) with scopes; OAuth 2.0 bearer/JWT
  for service-to-service access.
- Success: `202 Accepted` only after the message and queue rows are committed.
- Errors: RFC 9457 `application/problem+json`.
- `Idempotency-Key` supported and required by default.
- From-domain authorization; no open relay.
- Optional signed webhooks for delivery events.
- JMAP `EmailSubmission` (RFC 8621) remains the standards-based alternative
  and must reuse the same submission service.

## Consequences

Positive:

- Applications can send mail with a simple HTTPS call.
- One queue, one signer, one retry/DSN implementation.
- Clear, testable error and idempotency semantics.
- Can add JMAP later without a second delivery path.

Negative:

- A non-standard API surface to version and document.
- API-key lifecycle and webhook SSRF are new security surfaces.
- Raw MIME callers can create malformed/complex messages; validation is
  critical.

Mitigations:

- Publish OpenAPI and run contract tests in CI.
- Hash API keys at rest; scope and rotate them.
- Validate/strip forbidden headers; never fetch caller-supplied URLs.
- Block private IPs for webhooks; sign payloads with HMAC.
- Keep the HTTP layer thin; all policy lives in the shared submission service.

## Alternatives considered

- **JMAP only:** too heavy for the first API and not a good fit for every
  backend.
- **SMTP only:** excludes HTTPS-only clients and serverless backends.
- **Direct provider relay:** defeats the purpose of building the server.
- **Separate HTTP delivery pipeline:** guarantees divergent behavior and bugs.
