# ADR 0006 — Authentication: Argon2id, SCRAM, OAuth2/OIDC

**Status:** Accepted
**Date:** 2026-11-11

## Context

Users authenticate to submission (587/465), IMAP (143/993), JMAP (443), and
ManageSieve (4190). Passwords must be stored safely; legacy clients need
widely supported SASL mechanisms; modern clients expect OAuth.

## Decision

- **Password storage:** Argon2id with per-user salt, memory-hard parameters
  calibrated to ~100–300 ms; store algorithm/params/salt/hash in one encoded
  string; rehash on login when parameters change.
- **SASL:** `SCRAM-SHA-256` (and `-PLUS` with TLS channel binding) preferred;
  `PLAIN` over TLS; `LOGIN` behind a legacy toggle; `OAUTHBEARER` for OAuth;
  `XOAUTH2` only if a required client needs it.
- **OAuth:** OIDC authorization code + PKCE for first-party clients; bearer
  tokens for JMAP; short-lived access tokens, refresh tokens, revocation.
- **Transport:** require TLS before any credentials; implicit TLS preferred.
- **Abuse controls:** per-user/IP rate limits, lockout, audit log, no secrets
  in logs.

## Consequences

Positive:

- Modern, memory-hard password storage.
- No plaintext password on the wire with SCRAM.
- OAuth avoids storing app passwords for modern clients.

Negative:

- Argon2id is CPU/memory expensive; size the auth worker pool.
- SCRAM `-PLUS` requires channel binding support and client compatibility.
- OAuth adds an identity provider and token lifecycle.

Mitigations:

- Cache verified auth results briefly; do not cache passwords.
- Rate-limit before hashing to avoid CPU DoS.
- Provide app passwords only if legacy clients require them, with scoped
  revocation.
- Test with Thunderbird, Outlook, Apple Mail, and K-9.

## Alternatives considered

- **bcrypt/PBKDF2:** acceptable, but Argon2id is the current recommendation.
- **Plain SASL only:** simpler, weaker; not acceptable for new deployments.
- **Custom token auth:** reinvents OAuth; avoid.
