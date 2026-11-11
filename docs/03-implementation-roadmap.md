# 03 — Implementation roadmap

This is the order of work. Each phase is a **vertical slice** with a demo,
tests, and an exit gate. Do not start the next phase until the gate passes.
The estimates are for one focused developer; multiply for part-time work.

**Rule:** every phase ends with a running system, not a half-merged layer.

---

## Phase 0 — Foundations (1–2 weeks)

**Objective:** a buildable, testable host with configuration, logging, and a
local dependency stack. No mail protocol code yet.

### Tasks

1. Create the solution layout from `00-scope-and-decisions.md`:
   `Host`, `Protocols.Smtp`, `Protocols.Imap`, `Protocols.Jmap`, `Mime`,
   `Auth`, `Transport`, `Queue`, `Storage`, `Delivery`, `Abuse`,
   `Observability`, plus test projects.
2. Add `Directory.Build.props`: `net10.0`, nullable, implicit usings,
   analyzers, warnings-as-errors in CI, deterministic builds.
3. Add `.editorconfig`, `dotnet format` check, and a CI workflow
   (`restore`, `build`, `test`, `format`).
4. Add `docker-compose.yml` with PostgreSQL, a local SMTP sink (Mailpit or
   `smtp-sink`), and optional rspamd/ClamAV.
5. Add configuration binding with validation-on-start:
   `Delily:Smtp`, `Delily:Imap`, `Delily:Storage`, `Delily:Tls`,
   `Delily:Queue`, `Delily:Auth`.
6. Add structured logging (Serilog) and OpenTelemetry traces/metrics.
7. Add `/health/live` and `/health/ready` for the HTTP host.
8. Add `docs/adr/0001`–`0007` (decision records).
9. Add a raw TCP test helper: connect, send bytes, read reply lines with
   timeouts. You will reuse it in every protocol test.
10. Add a `Directory.Build.rsp` only if you need it for restore; otherwise
    leave it alone.

### Exit gate

```bash
dotnet restore
dotnet build -warnaserror
dotnet test
docker compose up -d
curl -fsS http://localhost:8080/health/ready
```

- CI is green.
- No protocol listener is exposed to the public interface yet.
- A developer can run the stack with one command.

**Commit:** `chore: scaffold modular mail server solution`

### Risks

- Over-engineering the project layout. Keep one project per boundary, not per
  class.
- Adding a message broker "for later". Don't; PostgreSQL is enough.

---

## Phase 1 — SMTP inbound skeleton (2–3 weeks)

**Objective:** accept a message from `swaks` into a temporary store, with
correct framing, replies, limits, and no open relay.

### Tasks

1. Implement a TCP listener with `System.IO.Pipelines`; one session per
   connection; bounded concurrency.
2. Implement the session state machine:
   `Connected → Greeted → Identified → MailFrom → RcptTo → Data → Quit`.
3. Implement `EHLO`/`HELO`, `MAIL FROM`, `RCPT TO`, `DATA`, `RSET`, `NOOP`,
   `QUIT`, `VRFY`, `HELP`.
4. Advertise: `SIZE`, `8BITMIME`, `PIPELINING`, `ENHANCEDSTATUSCODES`,
   `SMTPUTF8`, `CHUNKING`, `STARTTLS` (if configured), `LIMITS`.
5. Implement dot-stuffing/unstuffing, CRLF framing, line limits, max size,
   max recipients, and per-command timeouts.
6. Implement reply formatting: multi-line replies, enhanced status codes,
   stable message text.
7. Implement relay policy: local domains accepted; everything else
   `550 5.7.1 Relay access denied`.
8. Store accepted messages in a temp spool with envelope metadata; no
   database yet.
9. Add tests: `swaks`, raw socket tests, malformed commands, oversized lines,
   smuggling payloads, pipelining.
10. Add connection/command rate limits and a 421 on overload.

### Exit gate

```bash
swaks --to user@local.test --server 127.0.0.1:2525 --body 'hello'
swaks --to user@example.com --server 127.0.0.1:2525   # must be refused
dotnet test --filter Category=Smtp
```

- `swaks` succeeds and the message is visible in the spool.
- A relay attempt is refused with `550`.
- A bare-LF `DATA` terminator does not create a second message.
- Command line > 512 octets gets `500 5.5.2`.
- `DATA` before `RCPT` gets `503`.
- The session survives `RSET` and multiple transactions.

**Commit:** `feat(smtp): add inbound session state machine`

### Cues

- Read RFC 5321 §4.1–4.5 before coding.
- Write the state machine as an explicit `enum` + transition function; table-
  driven tests are much easier.
- Do not use `StreamReader.ReadLine()` for protocol framing: it accepts bare
  LF and hides the octet count.

---

## Phase 2 — Durable store and local delivery (2 weeks)

**Objective:** accepted mail survives a restart and appears in a mailbox.

### Tasks

1. Add PostgreSQL + EF Core (or Dapper) with migrations.
2. Implement the blob store (`sha256` path, temp + rename + fsync).
3. Implement `domains`, `users`, `aliases`, `mailboxes`, `messages`,
   `message_mailboxes`.
4. Make acceptance transactional: blob + message + queue row in one logical
   commit; `250` only after commit.
5. Implement local routing: exact user, alias, catch-all, plus-addressing,
   case rules.
6. Implement quota check at delivery; reject with `452 4.2.2` when full.
7. Implement idempotency: same queue key/message hash does not duplicate.
8. Add a GC job for orphan blobs and a checksum scrub job.
9. Add backup/restore of DB + blobs in the dev stack.

### Exit gate

- Kill the process after `250`; restart; the message is still deliverable.
- Kill the process before `250`; restart; no half-message is visible.
- Fill the disk (or use a quota) and verify `452`/`451`, not data loss.
- A duplicate delivery attempt does not create two mailbox rows.
- Blob GC does not delete a referenced blob.

**Commit:** `feat(storage): persist messages and deliver to local mailboxes`

---

## Phase 3 — Submission and authentication (2 weeks)

**Objective:** an authenticated user can submit mail on 587/465; anonymous
relay is impossible.

### Tasks

1. Add a submission listener (587 STARTTLS, 465 implicit TLS).
2. Require TLS before `AUTH` (RFC 8314).
3. Implement `AUTH PLAIN`, `AUTH LOGIN` (legacy toggle), and
   `AUTH SCRAM-SHA-256` (and `-PLUS` if channel binding is available).
4. Store passwords with Argon2id; add rehash-on-login.
5. Enforce `MAIL FROM` authorization: the authenticated user may use their
   own addresses/aliases; otherwise `550 5.7.1`.
6. Add `Date`, `Message-ID`, and `From` validation per RFC 6409.
7. Add submission rate limits, max recipients, max size, and brute-force
   lockout.
8. Add audit logging for auth success/failure (no passwords).
9. Add tests with `swaks --auth`, Thunderbird, and a raw SASL client.

### Exit gate

- `swaks --auth-user ... --auth-password ...` succeeds over 587 and 465.
- Plaintext `AUTH` is refused.
- A authenticated user cannot send as another domain.
- 10 failed logins trigger a temporary lockout.
- `MAIL FROM` without auth is refused on submission.

**Commit:** `feat(submission): add authenticated message submission`

---

## Phase 4 — IMAP read/write path (3–4 weeks)

**Objective:** a real client can read, flag, search, and delete mail.

### Tasks

1. Implement the IMAP parser: tagged/untagged responses, literals
   (`{n}`, `{n+}`), quoted strings, parenthesized lists, NIL.
2. Implement states: not authenticated, authenticated, selected, logout.
3. Implement `CAPABILITY`, `LOGIN`, `AUTHENTICATE`, `STARTTLS`, `NOOP`,
   `LOGOUT`.
4. Implement `LIST`, `LSUB`, `STATUS`, `SELECT`, `EXAMINE`, `CREATE`,
   `DELETE`, `RENAME`, `SUBSCRIBE`, `UNSUBSCRIBE`.
5. Implement `FETCH` (envelope, flags, internaldate, size, body sections,
   `BODY.PEEK`), `STORE`, `SEARCH`, `COPY`, `MOVE`, `EXPUNGE`, `APPEND`.
6. Implement UID commands, `UIDVALIDITY`, `UIDNEXT`, `MODSEQ`, flags,
   keywords, `\Recent`, `\Deleted`.
7. Add `IDLE`, `LITERAL+`, `UIDPLUS`, `SPECIAL-USE`, `NAMESPACE`, `QUOTA`,
   `ENABLE`, `CONDSTORE/QRESYNC` (in that order).
8. Implement mailbox hierarchy with a configurable delimiter and
   case-insensitive `INBOX`.
9. Add concurrency tests: two sessions, flag changes, expunge, `IDLE`.
10. Run `imaptest` and Thunderbird against it.

### Exit gate

- Thunderbird can add the account, list folders, read a message, flag it,
  move it, and delete it.
- `imaptest` passes the implemented command subset.
- Two sessions see flag changes consistently.
- `UIDVALIDITY` survives restart; `UIDNEXT` never decreases.
- A malformed literal does not desynchronize the parser.

**Commit:** `feat(imap): add IMAP4rev1 mailbox access`

### Cues

- IMAP is a **stateful** protocol; model state explicitly.
- Sequence numbers are ephemeral. Do all internal work in UIDs.
- Never send an untagged `EXPUNGE` for a message the client has not seen in
  the current session without considering `QRESYNC`.
- `BODY[]` returns the raw message; `BODY[TEXT]` excludes headers. Get this
  right or clients will show corrupted messages.

---

## Phase 5 — Outbound queue and delivery (3 weeks)

**Objective:** mail submitted locally reaches a remote SMTP server, with
retries and correct bounces.

### Tasks

1. Implement the queue schema and claim loop (`FOR UPDATE SKIP LOCKED`).
2. Implement MX resolution, implicit MX, null MX, CNAME handling, IPv4/IPv6
   preference, and multiple-MX fallback.
3. Implement the SMTP client: `EHLO`, `STARTTLS`, `MAIL`, `RCPT`, `DATA`,
   `RSET`, `QUIT`; parse multi-line replies and enhanced codes.
4. Add `SIZE`, `8BITMIME`, `SMTPUTF8`, `PIPELINING`, `BDAT` where advertised.
5. Add retry schedule with jitter, per-domain concurrency, and circuit
   breakers.
6. Generate DSNs (RFC 3464) with correct `Action`, `Status`, `Diagnostic-Code`,
   `Remote-MTA`, and `Original-Recipient`/`Final-Recipient`.
7. Handle null reverse-path: never bounce a bounce; send double bounces to
   `postmaster`.
8. Add loop detection: `Received` count, `Auto-Submitted`, `X-Loop`,
   duplicate `Message-ID`.
9. Add queue inspection/pause/requeue admin commands.
10. Test against Mailpit/Postfix and simulated 4xx/5xx/timeouts.

### Exit gate

- A message to a local Postfix sink arrives with correct headers.
- A 450 response is retried with backoff; a 550 produces a DSN.
- A DSN has a null reverse-path and valid `message/delivery-status`.
- Killing the worker mid-delivery does not lose the queue row.
- A domain with null MX produces a permanent failure, not a retry loop.

**Commit:** `feat(queue): add durable outbound delivery and DSNs`

---

## Phase 6 — Email authentication (3 weeks)

**Objective:** verify inbound SPF/DKIM/DMARC/ARC and sign outbound mail.

### Tasks

1. Add a DNS resolver abstraction with caching, timeouts, EDNS, and
   DNSSEC-aware results (use a validating resolver if possible).
2. Implement SPF evaluation (RFC 7208): mechanisms, modifiers, macros,
   lookup limits, void lookups, `redirect`, `exp`, HELO identity.
3. Implement DKIM verification: parse `DKIM-Signature`, canonicalize,
   body hash, key lookup, RSA/Ed25519, `l=` handling, key revocation.
4. Implement DKIM signing for outbound: selector management, header
   selection, oversigning, 2048-bit RSA and Ed25519.
5. Implement DMARC (RFC 9989): record discovery, organizational domain via
   PSL, alignment, `p/sp/np/t/psd`, disposition, local override.
6. Implement aggregate report generation (RFC 9990) and failure report
   handling (RFC 9991) with external-destination authorization.
7. Implement ARC verification and sealing (RFC 8617), gated behind a flag.
8. Add `Authentication-Results` and `Received-SPF` headers; strip untrusted
   inbound copies.
9. Add test vectors: known-good and known-bad SPF/DKIM/DMARC messages.

### Exit gate

- Gmail/Yahoo show `SPF: pass`, `DKIM: pass`, `DMARC: pass` for a signed
  outbound message.
- A forwarded message with a broken SPF but valid DKIM still passes DMARC
  under relaxed alignment.
- An unsigned message from a `p=reject` domain is rejected/quarantined.
- An ARC chain from a trusted forwarder validates; a tampered chain fails.
- DMARC reports are generated with valid XML and sent to `rua`.

**Commit:** `feat(auth): add SPF, DKIM, DMARC, and ARC`

---

## Phase 6b — HTTP submission API (2 weeks)

**Objective:** authenticated applications can send mail over HTTPS and get a
message ID and delivery status, using the same queue and signing path as SMTP
submission.

**Depends on:** Phase 2 (durable store), Phase 5 (queue), Phase 6 (DKIM).

### Tasks

1. Add the `Delily.Api` endpoint group:
   - `POST /api/v1/messages` (JSON);
   - `POST /api/v1/messages/raw` (`message/rfc822`);
   - `GET /api/v1/messages/{id}`.
2. Implement API keys:
   - `dlly_<env>_<public-id>_<secret>` format;
   - hash secrets at rest; lookup by public ID; constant-time compare;
   - scopes, expiry, revocation, rotation overlap.
3. Add OAuth 2.0 bearer/JWT validation if service-to-service access is needed.
4. Enforce From-domain authorization and tenant isolation. Cross-tenant
   lookups return `404`.
5. Validate the JSON schema and raw MIME:
   - recipients, sizes, attachments, header allowlist;
   - reject CRLF injection and forbidden trace headers;
   - decode base64 with limits; sanitize filenames; never fetch URLs.
6. Build MIME, add `Date`/`Message-ID`, remove `Bcc`, DKIM-sign, and write the
   message + queue rows in one transaction before returning `202`.
7. Implement `Idempotency-Key` (same key + same body -> original response;
   same key + different body -> `409`).
8. Implement error responses with RFC 9457 `application/problem+json`.
9. Add per-key/per-tenant rate limits and `429` with `Retry-After`.
10. Add OpenAPI metadata and contract tests.
11. Add status lookup and, if in scope, signed webhooks with SSRF protection.

### Exit gate

```bash
curl -i -X POST https://localhost:5001/api/v1/messages \
  -H "Authorization: Bearer $DELILY_API_KEY" \
  -H "Idempotency-Key: $(uuidgen)" \
  -H "Content-Type: application/json" \
  --data @message.json
```

- Valid request -> `202`, message delivered through the normal queue.
- Missing/invalid key -> `401`; wrong tenant/domain -> `403`.
- No recipients -> `422`; oversized body -> `413`; CRLF header -> `400`.
- Same idempotency key + same body -> one message; different body -> `409`.
- Queue unavailable -> `503`, never `202`.
- Status for another tenant -> `404`.
- OpenAPI document is generated and contract tests pass.
- No message body or credential appears in logs.

**Commit:** `feat(api): add authenticated HTTP message submission`

---

## Phase 7 — Transport security hardening (2 weeks)

**Objective:** TLS is enforced where policy requires it, and failures are
reported.

### Tasks

1. Enforce TLS 1.2+ only (prefer 1.3); disable 1.0/1.1 and legacy ciphers.
2. Add MTA-STS policy fetch/cache/enforce, with `mode`, `max_age`, `mx`
   matching, and `id` validation.
3. Add TLS-RPT report generation and sending.
4. Add DANE/TLSA verification when DNSSEC validation is available; never
   downgrade on a TLSA mismatch.
5. Add `REQUIRETLS` handling (RFC 8689) for outbound.
6. Add certificate management: ACME, renewal, CAA, SNI, and expiry alerts.
7. Add SMTP smuggling mitigations and tests.
8. Add `testssl.sh`/`sslyze` checks to CI or a scheduled job.

### Exit gate

- `testssl.sh` reports no TLS 1.0/1.1 and no known-bad ciphers.
- A domain with `mta-sts` `mode: enforce` is not delivered to over
  plaintext; a cert mismatch defers/fails.
- A TLSA mismatch is not downgraded.
- TLS-RPT reports are accepted by a test collector.
- Smuggling payloads do not produce two messages.

**Commit:** `feat(tls): enforce MTA-STS, DANE, and TLS reporting`

---

## Phase 8 — Abuse, spam, and deliverability (ongoing)

**Objective:** inbound spam is contained; outbound mail lands in the inbox.

### Tasks

1. Add a filter pipeline: connection reputation → DNSBL → SPF/DKIM/DMARC →
   greylisting → content filter → virus scan → quarantine.
2. Integrate rspamd or SpamAssassin over a local socket; add ClamAV.
3. Add per-user/per-domain rate limits and outbound volume caps.
4. Add ARF/FBL processing, bounce classification, and suppression lists.
5. Add `List-Unsubscribe`/`List-Unsubscribe-Post` for list mail.
6. Add IP warmup plan, rDNS/HELO checks, blocklist monitoring, and
   Google Postmaster/Microsoft SNDS.
7. Add a quarantine UI/API and user release controls.
8. Add spam/ham corpora and regression tests.

### Exit gate

- `GTUBE` is quarantined; `EICAR` is blocked; normal mail passes.
- Outbound complaint rate is measurable and below the target.
- A blocklist listing triggers an alert with a runbook.
- Forwarded mail does not create backscatter.

**Commit:** `feat(abuse): add spam, virus, and reputation pipeline`

---

## Phase 9 — JMAP, Sieve, and advanced access (4 weeks)

**Objective:** a modern client can use JMAP; users can manage filters.

### Tasks

1. Implement JMAP core: session resource, capabilities, method calls,
   `using`, `createdIds`, back-references, `ifInState`, errors.
2. Implement JMAP Mail: `Mailbox`, `Thread`, `Email`, `Email/query`,
   `Email/get`, `Email/set`, `Email/import`, `EmailSubmission`, `Identity`,
   `VacationResponse`.
3. Implement blobs (RFC 9404), quotas (9425), and sharing (9670) if needed.
4. Implement push: SSE, WebSocket (8887), WebPush with VAPID (9749).
5. Implement ManageSieve (5804) and JMAP Sieve (9661).
6. Add `JMAPACCESS` to IMAP (9698).
7. Run the JMAP test suite and a real client.

### Exit gate

- JMAP session discovery works over HTTPS.
- A client can list mailboxes, query messages, fetch a body, submit mail,
  and set flags.
- `ifInState` conflicts return `stateMismatch`, not data loss.
- Sieve scripts upload, validate, activate, and take effect at delivery.

**Commit:** `feat(jmap): add JMAP mail and Sieve access`

---

## Phase 10 — Production readiness (2–4 weeks)

**Objective:** operable, recoverable, and defensible.

### Tasks

1. HA/failover for PostgreSQL; blob store replication or object storage.
2. Backup + restore drills with measured RTO/RPO.
3. Dashboards and alerts from `02-architecture.md` and `12-operations.md`.
4. Runbooks for disk full, queue backup, cert expiry, DNS failure, abuse
   spike, mail loop, compromise, and data breach.
5. Load tests: SMTP throughput, IMAP fetch, search, queue drain.
6. Security review: open relay, auth bypass, smuggling, header injection,
   SSRF, path traversal, TLS config, dependency audit.
7. Privacy/retention/legal review; publish abuse and privacy contacts.
8. Launch checklist (`checklists/launch-readiness.md`).
9. A staged rollout: one domain, one IP, low volume, monitor, expand.

### Exit gate

- Restore from backup into a clean environment succeeds within the target.
- A simulated disk-full event rejects mail without losing accepted mail.
- A simulated cert expiry alerts before impact.
- An external security review finds no critical/high issues.
- The launch checklist is signed off.

**Commit:** `chore: harden for production launch`

---

## Cross-phase work (do continuously)

| Activity | Cadence |
|---|---|
| Fuzz SMTP/IMAP/MIME parsers | Every phase; add corpus entries |
| Interop test with Thunderbird/Postfix/Gmail | End of each protocol phase |
| OpenAPI contract check (`/api/v1`) | Every API change |
| Update edge-case catalog | After every incident or bug |
| Dependency/security audit | Weekly |
| Backup restore test | Monthly |
| RFC map re-verification | Quarterly |
| Deliverability/blocklist check | Weekly once public |

---

## First 10 days (if you want to start tomorrow)

1. **Day 1:** create solution layout and CI; add raw TCP test helper.
2. **Day 2:** SMTP listener + greeting + `EHLO`/`HELO` + `NOOP`/`QUIT`.
3. **Day 3:** `MAIL`/`RCPT` parsing and relay policy.
4. **Day 4:** `DATA` streaming, dot-stuffing, limits, temp spool.
5. **Day 5:** reply formatting and error codes; `swaks` works.
6. **Day 6:** malformed-input tests; smuggling tests; timeouts.
7. **Day 7:** PostgreSQL schema + blob store.
8. **Day 8:** transactional accept + local delivery.
9. **Day 9:** restart/crash tests; idempotency.
10. **Day 10:** demo: `swaks` → delivered message survives restart.

If day 5 does not work, stop and fix the framing before adding features.

---

## Definition of done for every phase

- Code is formatted, builds with warnings-as-errors, and has tests.
- The protocol behavior is documented in the relevant doc.
- New edge cases are added to `14-edge-case-catalog.md`.
- Metrics/logs exist for the new path.
- A rollback or disable switch exists for risky behavior.
- The commit message is conventional and scoped.
