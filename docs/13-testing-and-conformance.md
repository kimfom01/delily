# 13 — Testing and conformance

Mail is an interoperability problem disguised as a protocol. Unit tests are
necessary but not sufficient: you need real clients, real MTAs, malformed
input, and failure injection.

---

## 1. Test layers

| Layer | What | Speed | Frequency |
|---|---|---|---|
| Unit | Parsers, state transitions, RFC algorithms | ms | Every commit |
| Property-based | Round-trip, idempotency, invariants | ms–s | Every commit |
| Integration | Listener + DB + blob store | s | Every PR |
| Protocol conformance | Raw socket scripts, golden corpus | s–min | Every PR |
| HTTP API conformance | OpenAPI schema, auth, idempotency, limits | s | Every PR |
| Interop | Real clients/MTAs/HTTP clients | min | Nightly |
| Load | Throughput, latency, queue drain | min–h | Weekly |
| Chaos | Crash, disk full, DNS/TLS failure | min | Weekly |
| Security | Open relay, injection, smuggling, TLS | min | Every release |
| Deliverability | External inbox placement | h–days | Per campaign |

---

## 2. Unit and property tests

Use xUnit + FsCheck (or CsCheck).

### SMTP

- Command parser: all valid/invalid forms; case-insensitivity; parameters.
- Address parser: quoted local parts, domain literals, source routes, EAI.
- State machine: every transition, including invalid ones.
- Reply formatter: multi-line, enhanced codes, length limits.
- Dot-stuffing round-trip.
- Timeout logic with a fake clock (`TimeProvider`).

### MIME

- Round-trip: parse → render → parse yields the same semantic tree.
- Canonicalization: known DKIM vectors.
- Header folding/unfolding; encoded words; RFC 2231.
- Boundary parsing; nested multipart; missing/duplicate boundaries.
- Base64/QP decoders reject invalid input without crashing.

### Auth

- SPF: table-driven vectors for every mechanism/qualifier/macro.
- DKIM: sign/verify round-trip; tamper each field; key sizes; Ed25519.
- DMARC: alignment matrix; policy discovery; `np`/`psd`/`t`.
- ARC: chain validation, `cv`, instance ordering.

### IMAP

- Parser: literals, quoted strings, NIL, parenthesized lists.
- Sequence/UID translation under expunge.
- `FETCH` section parsing (`BODY[1.MIME]`, `HEADER.FIELDS`).
- Search criteria parsing and evaluation.

### Property ideas

- For any valid message, `Parse(Serialize(Parse(m)))` is equivalent.
- For any flag set, `STORE` then `FETCH` returns the same set.
- For any UID sequence, expunge never reuses a UID.
- For any queue row, retry never loses the message.

---

## 3. Protocol conformance harness

A raw TCP client is the most valuable test tool you can build:

```csharp
await using var client = await SmtpTestClient.ConnectAsync(port);
await client.ReadGreetingAsync();                 // 220
await client.SendAsync("EHLO test.example");
var ehlo = await client.ReadMultilineAsync();    // 250-...
await client.SendAsync("MAIL FROM:<a@example.com>");
await client.SendAsync("RCPT TO:<b@local.test>");
await client.SendAsync("DATA");
await client.ReadAsync();                         // 354
await client.SendRawAsync("Subject: test\r\n\r\nhello\r\n.\r\n");
await client.ReadAsync();                         // 250
```

Assert on exact codes, enhanced codes, and state. Add a `SlowClient` that
sends one byte at a time to test framing.

### Golden corpus

Store under `tests/corpus/`:

```
smtp/
  commands/valid/*.txt
  commands/invalid/*.txt
  data/dot-stuffing/*.eml
  data/smuggling/*.bin
mime/
  simple/*.eml
  multipart/*.eml
  malformed/*.eml
  eai/*.eml
auth/
  spf/*.json
  dkim/*.eml
  dmarc/*.json
  arc/*.eml
imap/
  commands/*.txt
  literals/*.bin
```

Each case has an expected output/defect list. No real PII.

---

## 4. Tools

| Tool | Use |
|---|---|
| `swaks` | Ad-hoc SMTP/submission tests, auth, TLS |
| `smtp-sink` (Postfix) | Capture SMTP conversations |
| `smtp-source` | Load generation |
| `imaptest` (Dovecot) | IMAP conformance/load |
| JMAP Test Suite | JMAP method conformance |
| `dkimpy` | DKIM sign/verify, test vectors |
| `opendkim-testmsg` | DKIM verification |
| `spfquery` | SPF evaluation |
| `checkdmarc` | DMARC/DNS validation |
| OpenARC | ARC seal/verify |
| `testssl.sh` / `sslyze` | TLS configuration |
| `openssl s_client` | STARTTLS/cert inspection |
| `dig` / `kdig` / `delv` | DNS/DNSSEC |
| `danetest` | DANE validation |
| `rspamd` / `spamassassin` | Spam scoring |
| ClamAV | Virus scanning |
| Mailpit / MailHog | Local SMTP sink/UI |
| GreenMail | Java test SMTP/IMAP server |
| Postfix / Dovecot | Interop peers |
| Thunderbird / K-9 / Apple Mail | Client interop |
| `curl` / `jq` | JMAP |
| `tcpdump` | Wire-level debugging |
| `BenchmarkDotNet` | Microbenchmarks |
| `k6` | HTTP/JMAP load |
| Testcontainers | Postgres/Redis in tests |

### Example commands

```bash
# SMTP accept
swaks --to user@local.test --server 127.0.0.1:2525 --body 'hello'

# Submission with auth
swaks --to user@example.com --server 127.0.0.1:587 \
  --auth-user user@example.com --auth-password '...' --tls

# Relay must fail
swaks --to victim@example.net --server 127.0.0.1:2525

# IMAP conformance
imaptest host=127.0.0.1 port=143 user=user@example.com pass=... \
  mbox=INBOX secs=30

# DKIM
dkimpy -s selector1 -d example.com -k private.pem < message.eml > signed.eml
opendkim-testmsg < signed.eml

# DMARC
checkdmarc example.com

# TLS
testssl.sh --starttls smtp mx.example.com:25

# DANE
dig +dnssec _25._tcp.mx.example.com TLSA
```

---

## 5. Security tests

### Open relay

- From an external IP, `RCPT TO:<victim@example.net>` must be `550`.
- Try source routes, `%` hacks, `@` tricks, and `RCPT TO:<postmaster>`.
- Try after `STARTTLS`, after `AUTH` with a user from another domain.
- Try `MAIL FROM:<>` with an external recipient.

### Auth

- Plaintext `AUTH` before TLS -> `538`.
- Wrong password N times -> lockout/`454`.
- `AUTH` as another user's address -> `550` at `MAIL`.
- OAuth token replay/expiry.
- SASL cancellation (`*`) does not crash.

### SMTP smuggling

Send each payload and assert exactly one message:

```
\n.\r\n
\r\n.\n
\n.\n
..\n.\r\n
```

Also test `PIPELINING` with `DATA` and content in one packet.

### Header injection

- Display name with `\r\nBcc: victim@example.com`.
- Subject with CRLF.
- Filename with `../`.
- `Content-Type` parameter with CRLF.

### Resource exhaustion

- 10k connections (rate-limited, no OOM).
- 1 GB `DATA` with a declared small `SIZE` (reject safely).
- 1000 nested multipart (reject).
- Huge IMAP literal (reject, no desync).
- Slowloris (timeout).

### TLS

- TLS 1.0/1.1 rejected.
- Expired/self-signed cert rejected for MTA-STS/DANE.
- STARTTLS stripping: sender with `enforce` must not downgrade.
- DANE mismatch: fail, no plaintext.
- Certificate hostname mismatch: fail.

### HTTP submission API

- Missing/invalid/expired API key -> `401`.
- Valid key sends as an unauthorized domain/tenant -> `403`.
- No recipients -> `422`; oversized body/attachment -> `413`.
- CRLF in a display name/header/filename -> `400`.
- Same idempotency key + same body -> one message.
- Same key + different body -> `409`.
- Queue unavailable -> `503`, never `202`.
- Cross-tenant status lookup -> `404`.
- Rate limit -> `429` + `Retry-After`.
- Webhook to private/link-local/metadata IP -> blocked.
- Webhook HMAC verification and replay window.

### Web/JMAP

- CSRF on cookie auth.
- CORS wildcard with credentials.
- IDOR across tenants (`404`).
- Blob path traversal.
- JSON bomb / deeply nested JSON.
- Rate limit bypass via `X-Forwarded-For`.

---

## 6. Interop matrix

| Peer | Test |
|---|---|
| Postfix | Inbound/outbound, TLS, DSN, 8BITMIME, SMTPUTF8 |
| Exim | Same, plus pipelining/BDAT |
| Sendmail | Legacy compatibility |
| Dovecot | IMAP client behavior / migration |
| Gmail | Auth alignment, DMARC, TLS, bulk sender |
| Outlook/Microsoft 365 | Auth, TLS, DMARC, MIME |
| Yahoo | Bulk sender, FBL |
| Fastmail | JMAP/IMAP interop |
| Thunderbird | IMAP, IDLE, submission, search |
| Apple Mail | IMAP, special-use, push |
| K-9/FairEmail | IMAP, IDLE, EAI |
| Outlook mobile | OAuth, IMAP |
| `curl` / OpenAPI client | HTTP submission, status, errors |

Record the peer version, the exact command, and the trace. A screenshot is
not a bug report.

---

## 7. Load and performance

- **SMTP throughput:** messages/sec with 1 KiB, 100 KiB, 10 MiB bodies.
- **Concurrency:** 1k SMTP sessions, 10k IMAP sessions.
- **IMAP FETCH:** header-only vs full body vs attachment.
- **Search:** 100k messages, common and rare terms.
- **Queue drain:** backlog of 100k messages; measure drain rate and retry
  storms.
- **TLS:** handshakes/sec; session resumption.
- **DB:** write latency under WAL sync; read latency under concurrent IMAP.

Use BenchmarkDotNet for parsers and `k6`/custom clients for protocols. Set
SLOs before optimizing.

---

## 8. Chaos and failure injection

| Failure | Expected |
|---|---|
| Kill process after `250` | Message durable, deliverable |
| Kill process before `250` | No visible message; client retries |
| Kill delivery worker mid-attempt | Queue row returns to `queued` |
| Disk full | `452`/`451`, no accepted message lost |
| DB down | `451`, no accept; queue pauses |
| DNS timeout | Defer, retry, no bounce |
| TLS handshake failure | Policy-dependent defer/fail, no downgrade |
| Clock skew | UIDs/queue still monotonic; DSN dates sane |
| Network partition | Reconnect, no duplicate storm |
| Certificate expiry | Alert, defer/fail per policy |

Use `tc`, `iptables`, Toxiproxy, or a test double for DNS/TLS.

---

## 9. CI pipeline

```yaml
stages:
  - format
  - build
  - unit
  - integration
  - conformance
  - security
  - nightly-interop
```

- `dotnet format --verify-no-changes`
- `dotnet build -warnaserror`
- `dotnet test --filter Category!=Integration`
- Integration with Testcontainers (Postgres, Mailpit)
- Conformance corpus + fuzz smoke
- OpenAPI contract check for `/api/v1`
- `testssl.sh` against a container
- Nightly: Postfix/Dovecot/Thunderbird interop, load test

Release gate:

- [ ] All tests pass.
- [ ] No new compiler warnings.
- [ ] Security tests pass.
- [ ] No critical/high dependency vulnerabilities.
- [ ] Changelog and migration notes.
- [ ] Rollback plan.

---

## 10. Fuzzing

Fuzz the parsers, not the business logic:

- SMTP command line and DATA framing;
- MIME headers, boundaries, encodings;
- IMAP literals and command grammar;
- DKIM tag lists and base64;
- SPF macro expansion;
- JMAP JSON.

Use SharpFuzz (libFuzzer) or AFL. Keep a corpus and run for a fixed time in
CI; run longer nightly. A crash is a bug; a hang is a bug; an OOM is a bug.

---

## 11. Test data policy

- No real user mail, credentials, or PII.
- Generate synthetic messages with `MimeKit` or scripts.
- If you must use a real message, redact headers/body and get consent.
- Keep test keys separate from production; never reuse DKIM private keys.
- Store fixtures under version control only if they contain no secrets.

---

## 12. Definition of done

A protocol feature is done when:

- it has unit tests for valid/invalid inputs;
- it has at least one integration test through the real listener;
- it has been exercised by a real client or peer;
- its edge cases are in `14-edge-case-catalog.md`;
- its failure mode is observable (log/metric);
- it has a disable/rollback switch if risky.
