# 09 — Transport security

**Normative base:** RFC 9846 (TLS 1.3), RFC 9325 + 9852 + 10015 (BCP 195),
RFC 8996 (no TLS 1.0/1.1), RFC 8314/8997 (email submission/access), RFC 3207
(STARTTLS), RFC 8689 (`REQUIRETLS`), RFC 8461 (MTA-STS), RFC 8460 (TLS-RPT),
RFC 7672/7671/6698 (DANE), RFC 9525 (service identity), RFC 5280 (X.509),
RFC 8659 (CAA), RFC 8555/8737 (ACME).

This document covers the transport between MTAs and between clients and
servers. Message-level authentication is in `08-email-authentication.md`.

---

## 1. TLS baseline

| Setting | Recommendation |
|---|---|
| Minimum version | TLS 1.2 for SMTP/IMAP/POP interoperability; TLS 1.3 preferred |
| New protocol endpoints | TLS 1.3 required where BCP 195/RFC 9852 applies |
| Disabled | TLS 1.0, TLS 1.1, SSLv3, SSLv2 |
| Cipher suites | AEAD only: AES-GCM, ChaCha20-Poly1305; forward secrecy |
| Key exchange | ECDHE; disable weak/legacy groups per RFC 10015 |
| Certificates | ECDSA P-256 or RSA 2048+; SHA-256; SANs for every hostname |
| Renegotiation | Disabled |
| Compression | Disabled |
| Session resumption | Allowed; ticket keys rotated |
| OCSP stapling | Recommended |
| SNI | Required for multi-host certificates |

TLS 1.2 is still needed because much of the SMTP ecosystem has not moved to
1.3. Do not interpret "prefer 1.3" as "reject 1.2" for port 25; you will lose
mail. For a brand-new HTTPS/JMAP endpoint, requiring 1.3 is defensible; make
it configurable and measure client support.

### Certificate identity

RFC 9525 replaced RFC 6125. Verify the certificate against the **service
hostname**:

- SMTP submission/IMAP/POP: the hostname the client configured, or the SRV
  target.
- MTA-to-MTA: the MX hostname (not the recipient domain).
- MTA-STS policy fetch: `mta-sts.<policy-domain>`.
- JMAP/admin: the HTTP hostname.

Do not disable chain or hostname validation to make a broken server work.
Log the failure, defer, and alert.

---

## 2. STARTTLS (RFC 3207)

- Advertise `STARTTLS` in `EHLO` only when TLS is configured.
- On `STARTTLS`, reply `220 Ready to start TLS`, then perform the handshake.
- After a successful handshake, **discard all prior state**: HELO/EHLO,
  envelope, authentication, and capabilities. Require a new `EHLO`.
- Do not allow `AUTH` before TLS on submission.
- A failed handshake closes the connection; do not continue in plaintext.
- STARTTLS is vulnerable to stripping if the client does not require TLS;
  MTA-STS, DANE, and `REQUIRETLS` mitigate this.

### Implicit TLS

Offer implicit TLS on:

| Port | Protocol |
|---|---|
| 465 | SMTP submission (`submissions`) |
| 993 | IMAP over TLS (`imaps`) |
| 995 | POP3 over TLS (`pop3s`) |

RFC 8314 recommends implicit TLS for submission and access. Keep STARTTLS on
587/143 for compatibility, but prefer implicit TLS in documentation and
client autoconfig.

---

## 3. MTA-STS (RFC 8461)

MTA-STS lets a recipient domain publish a policy that senders must use
PKIX-authenticated TLS to its MX hosts.

### DNS record

```
_mta-sts.example.com.  TXT  "v=STSv1; id=20261111000000Z;"
```

- `v=STSv1` must be first.
- `id` changes when the policy changes; senders use it to detect updates.
- The record must be at `_mta-sts.<domain>`; no CNAME tricks.

### Policy

`https://mta-sts.example.com/.well-known/mta-sts.txt`:

```
version: STSv1
mode: enforce
mx: mail.example.com
mx: *.example.net
max_age: 604800
```

| Field | Meaning |
|---|---|
| `version` | `STSv1` |
| `mode` | `enforce`, `testing`, or `none` |
| `mx` | One or more MX host patterns; `*` wildcard allowed as a label |
| `max_age` | Cache lifetime in seconds; max 31557600 |

### Fetching rules

- HTTPS only; valid certificate for `mta-sts.<domain>` (RFC 9525 rules).
- HTTP 200 only; **do not follow 3xx redirects**; do not use HTTP caching.
- Timeout ~1 minute; max policy size ~64 KiB.
- On fetch failure with no valid cached policy: proceed as if MTA-STS is not
  implemented (log).
- On fetch failure with a valid cached policy: apply the cached policy.
- Refresh proactively (daily is suggested) before expiry.
- Cache keyed by domain and `id`; do not apply a policy for a different
  domain.

### Application

- `mode: enforce`: if TLS cannot be established/validated to a matching MX,
  do not deliver in plaintext; defer/bounce according to policy.
- `mode: testing`: attempt TLS; on failure, deliver but report.
- `mode: none`: no enforcement; still report if TLS-RPT is configured.
- Match the MX hostname against `mx` patterns; wildcard `*.example.com`
  matches one or more labels? Treat per RFC and test; do not over-match.
- MTA-STS does not replace DANE; if both exist, DANE is the stronger signal.

### Publishing (receiver side)

- Publish `_mta-sts` TXT and the HTTPS policy.
- Use `mode: testing` first, monitor TLS-RPT, then `enforce`.
- Keep `max_age` moderate (days, not a year) during rollout.
- Ensure every MX host has a valid certificate for its name.
- Keep the policy host highly available; a fetch failure weakens protection.

---

## 4. DANE (RFC 7672)

DANE uses DNSSEC to publish TLSA records for MX hosts.

### Record

```
_25._tcp.mail.example.com. IN TLSA 3 1 1 <sha256-of-SPKI>
```

| Field | Common value |
|---|---|
| Usage | `3` (DANE-EE, end entity) |
| Selector | `1` (SPKI) |
| Matching | `1` (SHA-256) |

### Rules

- Require DNSSEC validation; an insecure TLSA response is not DANE.
- Look up `_<port>._tcp.<mx-host>` (port 25 for SMTP).
- If a secure TLSA RRset exists, the certificate must match; a mismatch is a
  hard failure. Never downgrade.
- If no TLSA RRset exists, DANE does not apply; use MTA-STS/opportunistic TLS.
- Support multiple TLSA records for rollover; try each.
- CNAMEs in the TLSA query are followed only if the entire chain is secure.
- DANE is stronger than MTA-STS because it is anchored in DNSSEC, not the Web
  PKI. If both are available, enforce both; a conflict is a policy decision,
  but never silently downgrade.

### Publishing

- Enable DNSSEC for the MX zone.
- Publish `3 1 1` with the SPKI hash.
- During key rollover, publish both old and new records before switching.
- Monitor with `danetest`/`delv`; a broken TLSA record causes mail loss.

---

## 5. TLS-RPT (RFC 8460)

TLS-RPT lets a domain receive reports about TLS failures from senders.

### DNS record

```
_smtp._tls.example.com.  TXT  "v=TLSRPTv1; rua=mailto:tlsrpt@example.com"
```

### Report

JSON, usually gzipped, sent daily (or on failure) to the `rua` address:

```json
{
  "organization-name": "Example",
  "date-range": { "start-datetime": "...", "end-datetime": "..." },
  "contact-info": "tlsrpt@example.com",
  "report-id": "...",
  "policies": [
    {
      "policy": {
        "policy-type": "sts",
        "policy-string": ["version: STSv1", "mode: enforce", "..."],
        "policy-domain": "example.com",
        "mx-host": ["mail.example.com"]
      },
      "summary": {
        "total-successful-session-count": 100,
        "total-failure-session-count": 2
      },
      "failure-details": [
        {
          "result-type": "certificate-expired",
          "sending-mta-ip": "192.0.2.10",
          "receiving-mx-hostname": "mail.example.com",
          "failed-session-count": 2
        }
      ]
    }
  ]
}
```

- Report only for policies you publish (MTA-STS, DANE, etc.).
- Do not include message content or user identifiers.
- Rate-limit and aggregate; do not let a report flood your inbound queue.
- Use reports to fix certs/policy before switching to `enforce`.

---

## 6. `REQUIRETLS` (RFC 8689)

`MAIL FROM:<...> REQUIRETLS` means the message must not be delivered over
plaintext or downgraded. If TLS cannot be established, return a permanent
failure (or a DSN with the appropriate status). Do not strip the parameter
when relaying.

This is a per-message policy; it complements MTA-STS/DANE, which are
per-domain.

---

## 7. SMTP smuggling and framing

The detailed attack is in `04-smtp-inbound.md`. Transport rules:

1. Command and DATA parsers must agree on `<CRLF>.<CRLF>` as the only
   end-of-data marker.
2. Reject or normalize bare LF in commands.
3. Dot-stuff every outbound line that begins with a dot.
4. Do not pass through a body containing a bare-LF dot terminator without
   normalization/rejection.
5. Do not let `PIPELINING` blur the boundary between `DATA` and message
   content.

Test payloads:

```
\n.\r\n
\r\n.\n
\n.\n
..\n.\r\n
```

A correct server produces one message, not two, and never interprets the
trailing bytes as commands.

---

## 8. Certificate management

### What you need

| Hostname | Used by | Certificate |
|---|---|---|
| `mx.example.com` | inbound/outbound SMTP | SAN for every MX name |
| `mail.example.com` | IMAP/POP/submission | SAN; clients configure this |
| `mta-sts.example.com` | MTA-STS policy | SAN; must be valid |
| `jmap.example.com` | JMAP/admin | SAN; HSTS |
| `autoconfig.example.com` | autodiscovery | SAN |

A wildcard `*.example.com` can cover many of these, but not
`example.com` itself and not every MX name outside the zone. Use SANs.

### ACME

- HTTP-01 needs port 80 reachable; DNS-01 works for wildcards and hosts
  without inbound HTTP; TLS-ALPN-01 uses port 443.
- Automate renewal and reload; alert at 14/7/3 days.
- Keep account keys and private keys in a secret manager.
- Publish CAA:

```
example.com. CAA 0 issue "letsencrypt.org"
example.com. CAA 0 issuewild "letsencrypt.org"
example.com. CAA 0 iodef "mailto:security@example.com"
```

- CAA applies to the exact domain and parent; test with `dig CAA`.

### HSTS

For HTTPS endpoints (JMAP, admin, autoconfig, MTA-STS policy host):

```
Strict-Transport-Security: max-age=31536000; includeSubDomains
```

Only add `preload` if you are certain every subdomain is HTTPS-only.

---

## 9. HTTP security (JMAP/admin)

- TLS only; redirect HTTP to HTTPS (except ACME challenges).
- HSTS.
- `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`.
- `X-Content-Type-Options: nosniff`.
- `Referrer-Policy: no-referrer`.
- CSRF protection for cookie-based sessions; `SameSite=Lax/Strict`.
- CORS allowlist; no wildcard with credentials.
- Rate-limit login, upload, and query endpoints.
- Do not expose admin APIs on the public interface; use a private network or
  VPN.
- Validate `Content-Type`, cap request body, and reject unknown methods.

---

## 10. DNS security

- Use a validating resolver (DNSSEC) for SPF/DKIM/DMARC/DANE/MTA-STS.
- Do not trust a recursive resolver that strips the AD bit without local
  validation.
- Protect against DNS rebinding/SSRF: when fetching MTA-STS policies or
  remote URLs, block private/link-local/metadata IPs unless explicitly
  allowed.
- Cache DNS with TTL; do not cache negative results longer than the SOA
  minimum.
- Monitor DNSSEC expiry and key rollover.

---

## 11. Security testing

```bash
# TLS configuration
testssl.sh --starttls smtp mx.example.com:25
testssl.sh mail.example.com:993
sslyze --regular mail.example.com:443

# STARTTLS handshake
openssl s_client -starttls smtp -connect mx.example.com:25 -servername mx.example.com

# Certificate chain
openssl s_client -connect mx.example.com:25 -starttls smtp -showcerts

# DANE
dig +dnssec _25._tcp.mail.example.com TLSA
danetest mail.example.com

# MTA-STS
curl -fsS https://mta-sts.example.com/.well-known/mta-sts.txt
dig +short TXT _mta-sts.example.com

# TLS-RPT
dig +short TXT _smtp._tls.example.com

# CAA
dig +short CAA example.com
```

Checklist:

- [ ] TLS 1.0/1.1 rejected on every listener.
- [ ] TLS 1.2/1.3 work with modern clients.
- [ ] Certificate chain and hostname validate.
- [ ] STARTTLS resets state and requires a new `EHLO`.
- [ ] MTA-STS `enforce` prevents plaintext delivery.
- [ ] DANE mismatch fails; no downgrade.
- [ ] `REQUIRETLS` honored.
- [ ] TLS-RPT reports arrive and parse.
- [ ] SMTP smuggling payloads produce one message.
- [ ] CAA restricts issuance.
- [ ] HSTS/CSRF/CORS on HTTP endpoints.
- [ ] No private-IP SSRF via policy fetch.
