# ADR 0007 — TLS termination in-process

**Status:** Accepted
**Date:** 2026-11-11

## Context

SMTP, IMAP, POP3, and ManageSieve are raw TCP protocols with STARTTLS or
implicit TLS. A generic TCP proxy that terminates TLS can work, but it:

- hides the client certificate and TLS metadata from the protocol server;
- complicates STARTTLS state resets;
- makes per-listener policy (versions, ciphers, SNI) harder;
- adds a hop and a failure domain.

## Decision

Terminate TLS **in-process** with `SslStream` for SMTP/IMAP/POP3/ManageSieve.

- One certificate store, SNI selection by hostname.
- Per-listener TLS policy (protocol versions, ciphers, client certs).
- STARTTLS resets protocol state after the handshake.
- TLS metadata (version, cipher, SNI, client cert) is available to the
  session for logging/policy.
- Use a reverse proxy only for HTTP (JMAP/admin/autoconfig), where it is
  standard.

## Consequences

Positive:

- Accurate protocol state and observability.
- Per-listener policy without proxy configuration duplication.
- No extra network hop for mail protocols.

Negative:

- Certificate reload must be handled in-process.
- TLS handshake CPU is on the mail process; isolate/scale as needed.
- Less familiar to operators who expect a proxy.

Mitigations:

- Watch certificate files and hot-reload; keep the old cert until the new one
  is validated.
- Bound handshake concurrency and set handshake timeouts.
- Export TLS metrics (version, cipher, failures).
- Document that HTTP uses a reverse proxy, mail protocols do not.

## Alternatives considered

- **HAProxy/nginx `stream`:** viable, but loses client cert/protocol detail
  and complicates STARTTLS.
- **Stunnel:** extra process, same limitations.
- **TLS at the load balancer only:** breaks end-to-end client certs and
  per-listener policy.
