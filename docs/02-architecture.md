# 02 — Architecture

This is a reference architecture for a small-to-medium mail system that can
start as one process and split later. It is intentionally boring: mail is a
durability problem more than a throughput problem.

---

## 1. Component view

```
                         Internet
                            │
        ┌───────────────────┼────────────────────┐
        │                   │                    │
   :25 SMTP             :587/:465           :143/:993
   inbound MTA          submission           IMAP
        │                   │                    │
        ▼                   ▼                    ▼
 ┌─────────────┐     ┌─────────────┐      ┌─────────────┐
 │ SMTP session│     │ SMTP session│      │ IMAP session│
 │  + policy   │     │ + AUTH      │      │  + mailbox  │
 └──────┬──────┘     └──────┬──────┘      └──────┬──────┘
        │                   │                    │
        ▼                   ▼                    │
 ┌──────────────────────────────────┐            │
 │ message writer / auth pipeline   │            │
 │ (SPF, DKIM, DMARC, ARC, spam)    │            │
 └──────────────┬───────────────────┘            │
                │                                │
                ▼                                ▼
        ┌───────────────┐                ┌───────────────┐
        │ durable queue │                │ metadata DB   │
        │ (outbox)      │                │ (PostgreSQL)  │
        └───────┬───────┘                └───────┬───────┘
                │                                │
        ┌───────┴────────┐               ┌───────┴────────┐
        │ local delivery │               │ blob store     │
        │ (MDA + Sieve)  │──────────────▶│ (content-      │
        └───────┬────────┘               │  addressed)    │
                │                        └────────────────┘
                ▼
        ┌───────────────┐
        │ remote delivery│─── MX / TLS / MTA-STS / DANE ──▶ Internet
        │ worker         │
        └───────────────┘
```

Optional components:

- **HTTPS host** on 443: JMAP (session, mail, blobs, submission, push),
  the Delily REST submission API (`/api/v1/messages`), admin, and health.
- **ManageSieve** on 4190.
- **Admin/API** on localhost or a private network.
- **Spam/virus** sidecars (rspamd, ClamAV) over Unix sockets or localhost.
- **Object storage** (S3-compatible) for blobs at scale.
- **Redis** for rate limits and presence, never for the only copy of a
  message.

---

## 2. Process and deployment model

### Development / small deployment

One `Delily.Host` process starts:

- `SmtpInboundService` (25)
- `SubmissionService` (587, 465)
- `ImapService` (143, 993)
- `JmapService` (443 or behind a reverse proxy)
- `QueueWorker` and `DeliveryWorker`
- `MaintenanceWorker` (GC, reports, cert checks)

All share a PostgreSQL database and a local blob directory. This is easy to
debug and is the recommended starting point.

### Production

Split by **failure domain**, not by protocol fashion:

| Process | Scale signal | Notes |
|---|---|---|
| SMTP inbound | connections/sec, TLS handshakes | Keep close to the network edge |
| Submission (SMTP) | authenticated users | Same host as inbound is fine initially |
| HTTP submission API | API request rate, payload bytes | Stateless behind TLS; share the submission service and queue |
| IMAP | concurrent sessions, fetch bandwidth | Scale with mailbox size, not message rate |
| Queue worker | queue depth | Can run on a worker pool |
| Delivery worker | outbound concurrency, per-domain limits | Needs stable egress IPs |
| HTTP/JMAP | request rate | Stateless behind a load balancer |
| Maintenance | schedule | Single leader via advisory lock |

Use a **leader election** mechanism (PostgreSQL advisory lock is enough) for
singleton jobs: DMARC report generation, blob GC, certificate renewal,
queue compaction.

---

## 3. Inbound message flow (port 25)

1. **TCP accept**: assign a session ID, record peer IP/port, start a
   handshake timeout.
2. **Greeting**: `220 <fqdn> ESMTP Delily`.
3. **EHLO/HELO**: parse, record the claimed identity, advertise capabilities.
4. **Connection policy**: DNSBL, reputation, connection rate, max concurrent
   sessions, TLS requirement policy.
5. **MAIL FROM**: parse reverse-path, ESMTP parameters (`SIZE`, `BODY`,
   `SMTPUTF8`, `RET`, `ENVID`, `REQUIRETLS`). Apply sender policy.
6. **RCPT TO**: parse forward-path, `NOTIFY`, `ORCPT`. Validate local
   recipient or relay permission. Reject unknown local users here when the
   directory is authoritative; otherwise defer to DATA and bounce only if
   appropriate.
7. **DATA**: send `354`, stream the body to a temporary file while enforcing
   line and size limits. Do not buffer the whole message in memory.
8. **Framing**: require CRLF; handle dot-stuffing; detect bare LF/CR and
   decide normalize vs reject (see `09-transport-security.md`).
9. **Commit**: write the blob, insert message + envelope + queue rows in one
   database transaction. Only then send `250`.
10. **Post-accept pipeline**: authentication results, spam/virus, routing,
    local delivery or outbound queue. This may happen after `250`, but it must
    be durable and retryable.
11. **Trace**: prepend `Received`, add `Return-Path` (final delivery),
    `Delivered-To`, `Authentication-Results`.

**Important:** the SMTP session must not wait for spam scanning or remote
delivery. Accept, persist, acknowledge, then process asynchronously.

---

## 3.1 HTTP submission flow (443)

1. Authenticate the API key or OAuth token; resolve the tenant and scopes.
2. Authorize the `From` domain and envelope sender for that tenant.
3. Validate the JSON or raw MIME request; enforce size/recipient/attachment
   limits; reject header injection.
4. Build MIME (or accept the raw message), add `Date`/`Message-ID`, remove
   `Bcc`, DKIM-sign with the tenant selector.
5. Persist the message and queue rows in one transaction.
6. Return `202 Accepted` with a message ID and `Location`.
7. Delivery and status updates use the same queue/worker as SMTP submission.

The HTTP layer is a thin adapter over the same submission service. It must not
have its own delivery loop, its own DKIM implementation, or a separate queue.

## 4. Outbound flow

1. Submission (SMTP or HTTP) accepts an authenticated message.
2. Validate sender, recipients, size, and policy.
3. Add missing `Date`, `Message-ID`, and `From` checks (RFC 6409).
4. DKIM-sign with the domain's selector.
5. Persist the message and one queue row per recipient (or per recipient
   group with identical routing).
6. Delivery worker claims a batch with `FOR UPDATE SKIP LOCKED`.
7. Resolve MX, apply MTA-STS/DANE, connect, TLS, `EHLO`, `MAIL`, `RCPT`,
   `DATA`/`BDAT`.
8. Record per-recipient result; schedule retry or generate DSN.
9. On permanent failure, bounce with a null reverse-path; never bounce a
   null-sender message.

---

## 5. Data model

Start with these tables. Names are illustrative; keep the invariants.

### Identity and routing

```sql
domains(id, name, tenant_id, is_local, created_at)
users(id, domain_id, local_part, display_name, password_hash, status)
aliases(id, domain_id, local_part, destination, created_at)
mailboxes(id, user_id, name, parent_id, uidvalidity, uidnext,
          highest_modseq, attributes, created_at)
```

### Messages and mailbox membership

```sql
messages(
  id            uuid primary key,
  blob_hash     bytea not null,      -- SHA-256 of raw bytes
  size          bigint not null,
  envelope_from text not null,       -- may be empty for null sender
  received_at   timestamptz not null,
  header_json   jsonb,               -- parsed index, not the source of truth
  tenant_id     uuid not null
)

message_mailboxes(
  mailbox_id  uuid not null,
  uid         bigint not null,       -- per mailbox, monotonic
  message_id  uuid not null,
  flags       integer not null,      -- bitmask for system flags
  keywords    text[],                -- user keywords
  modseq      bigint not null,
  internaldate timestamptz not null,
  primary key (mailbox_id, uid)
)
```

`UIDVALIDITY` must be stable for the life of the mailbox. `UIDNEXT` must
never go backwards. Never reuse a UID after expunge.

### Queue

```sql
queue(
  id             uuid primary key,
  message_id     uuid not null,
  envelope_from  text not null,
  recipient      text not null,
  status         text not null,      -- queued|inflight|deferred|delivered|bounced|failed
  attempts       integer not null default 0,
  next_attempt_at timestamptz not null,
  last_error     text,
  dsn_ret        text,
  dsn_envid      text,
  dsn_notify     text,
  created_at     timestamptz not null,
  claimed_at     timestamptz,
  claimed_by     text
)
```

Index on `(status, next_attempt_at)` and `(recipient)`; use
`FOR UPDATE SKIP LOCKED` for workers.

### Security and policy cache

```sql
dkim_keys(domain_id, selector, private_key_ref, algorithm, created_at, retired_at)
tls_policy_cache(domain, policy_json, fetched_at, expires_at)
dmarc_reports(id, domain, report_id, received_at, xml_blob_hash)
auth_results(message_id, spf, dkim, dmarc, arc, details_json)
```

Store private keys in a KMS/secret manager, not in the database, if you can.
The `private_key_ref` is a pointer, not the key.

---

## 6. Blob store

Recommended layout:

```
data/blobs/sha256/ab/cd/abcdef...   # raw RFC 5322 bytes
data/tmp/<uuid>.eml                 # write here first
```

Write path:

1. Stream to `tmp/<uuid>.eml`, computing SHA-256 and size.
2. Enforce limits while streaming (do not trust `Content-Length`).
3. `fsync` the temp file.
4. Rename into the content-addressed path (atomic on the same filesystem).
5. `fsync` the directory.
6. Insert metadata in the DB, referencing the hash.
7. If the DB transaction fails, the blob becomes garbage; a GC job removes
   unreferenced blobs older than a grace period.

**Dedupe:** identical messages (same hash) share one blob. Be careful with
tenant isolation: two tenants may have byte-identical messages; sharing the
blob is fine if access control is enforced by metadata, but a per-tenant
encryption key would break dedupe. Decide explicitly.

**Deletion:** never delete a blob when one mailbox expunges it; delete when
the last reference is gone and the grace period has passed. Keep a tombstone
or audit record if policy requires it.

---

## 7. Queue semantics

- **At-least-once**, not exactly-once. A crash after `DATA` but before the
  remote `250` can duplicate a message. Make local delivery idempotent where
  possible (same `Message-ID` + envelope + mailbox within a window).
- **One row per recipient** makes partial failures and DSNs tractable. For
  very large fan-out, group recipients by domain and split only when a
  recipient fails.
- **Backoff:** exponential with jitter; see `05-smtp-outbound-queue.md`.
- **Visibility timeout:** a claimed row must return to `queued` if the worker
  dies. Use `claimed_at` + a heartbeat.
- **Poison messages:** after N attempts or a permanent 5xx, mark `failed` and
  generate a DSN. Never retry forever.
- **Per-domain limits:** cap concurrent connections and messages/sec to avoid
  being blocked. Keep a circuit breaker for repeated 4xx/timeouts.

---

## 8. IMAP/JMAP read path

- Authenticate, then open a session bound to a user and tenant.
- `SELECT` loads mailbox metadata (`UIDVALIDITY`, `UIDNEXT`,
  `HIGHESTMODSEQ`, `EXISTS`, `RECENT`, `FLAGS`, `PERMANENTFLAGS`).
- `FETCH` streams the blob; do not load the whole message for a header fetch.
  Cache `BODYSTRUCTURE` and `ENVELOPE` in the metadata DB or a sidecar table.
- Flag changes are small DB updates with a new `MODSEQ`; use a transaction and
  bump the mailbox `HIGHESTMODSEQ`.
- Expunge is a state change; notify other sessions (`* n EXPUNGE`) in UID
  order, or rely on `QRESYNC` for clients that support it.
- Search: start with SQL `tsvector` over extracted text; keep attachments out
  of the index unless explicitly enabled.
- Push: IMAP `IDLE` in-process; JMAP `EventSource`/WebSocket/WebPush. Do not
  hold a DB transaction while waiting.

---

## 9. Security boundaries

| Boundary | Threat | Control |
|---|---|---|
| TCP listener | Slowloris, connection flood | Per-IP limits, handshake/command timeouts, max sessions |
| SMTP parser | Command injection, smuggling | Strict CRLF, length limits, no string concatenation into replies |
| Message parser | Malformed MIME, zip bombs, XXE | MimeKit + limits, no external entity resolution, sandbox |
| Blob store | Path traversal | Never use client-supplied filenames; hashes only |
| IMAP parser | Literal bombs, sequence confusion | Max literal size, UID-first logic, fuzz tests |
| Auth | Brute force, credential stuffing | Rate limits, Argon2id, MFA, lockout, audit |
| HTTP submission API | Open relay, tenant escape, header injection | API-key/OAuth auth, From-domain authorization, schema validation, rate limits |
| Webhooks | SSRF, secret leak | HTTPS-only, private-IP deny, HMAC signatures, no redirects |
| Admin API | SSRF, tenant escape | Private network, auth, tenant-scoped queries |
| DNS | Spoofing, rebinding | Validating resolver, DNSSEC where available, no internal targets |
| TLS | Downgrade, cert mismatch | MTA-STS/DANE enforcement, strict validation, TLS 1.3 |
| Logs | PII/credential leak | Redact passwords, tokens, full bodies; retention policy |

---

## 10. Observability

Emit per message/session:

- `session_id`, `queue_id`, `message_id`, `tenant_id` (hashed where needed)
- peer IP, HELO, envelope from/to count, size
- SPF/DKIM/DMARC/ARC verdicts
- spam score and action
- delivery attempt, remote MX, TLS protocol/cipher, response code
- latency per phase (connect, TLS, EHLO, MAIL, RCPT, DATA, commit)

Metrics:

- active SMTP/IMAP sessions
- messages accepted/rejected by reason
- queue depth, oldest queued age, retry count
- delivery success rate by domain and response class
- auth failures by mechanism
- TLS handshake failures and policy mismatches
- blob store usage, DB size, WAL lag, disk free
- IMAP command latency p50/p95/p99

Alerts:

- queue age > 15 min (warning), > 1 h (page)
- disk < 15% free
- certificate expires < 14 days
- DMARC/TLS-RPT report volume anomaly
- auth failure spike / credential stuffing
- outbound 5xx spike or blocklist listing

---

## 11. Failure modes and required behavior

| Failure | Required behavior |
|---|---|
| DB unavailable before SMTP `250` | Return `451 4.3.0`; do not accept |
| DB/queue unavailable before HTTP `202` | Return `503`; do not accept |
| Blob write fails / disk full | Return `452 4.3.1` or `451`; do not accept |
| Crash after blob write, before DB commit | GC orphan blob; message was not accepted |
| Crash after DB commit, before `250` | Client may retry; dedupe by queue key/message hash |
| Crash after remote `250`, before local mark | Retry may duplicate; acceptable at-least-once |
| DNS temporary failure | Defer with 4xx; retry with backoff |
| DNS permanent failure (NXDOMAIN) | Bounce with 5.1.2/5.4.4 |
| TLS policy failure (MTA-STS enforce/DANE) | Do not downgrade; defer/bounce per policy |
| IMAP client disconnect mid-FETCH | No state corruption; literal framing must be exact |
| DB failover | Reconnect, retry idempotent operations, preserve UID invariants |

---

## 12. Architecture decisions to make early

1. **Blob store:** local filesystem vs S3-compatible. Start local; abstract.
2. **Metadata DB:** PostgreSQL vs SQLite. Start SQLite only for tests; use
   PostgreSQL for anything multi-user.
3. **Tenancy:** single-tenant vs multi-tenant. If multi-tenant, put
   `tenant_id` on every row and enforce it in a repository layer.
4. **IMAP rev1 vs rev2:** rev1 compatibility is mandatory; rev2 is a
   capability claim.
5. **MIME parser:** MimeKit vs own. See ADR 0004.
6. **Spam:** integrate rspamd or write rules. Do not write a Bayesian
   classifier before you can deliver mail.
7. **Search:** PostgreSQL FTS vs external. Start in Postgres.
8. **Queue:** Postgres outbox vs broker. Start Postgres; a broker adds a
   second durability domain.
9. **TLS termination:** in-process `SslStream`, not a TCP proxy.
10. **Deployment:** containers with persistent volumes; keep blobs and DB on
    separate volumes for backup/restore.

Record these in `adr/`. The rest of the docs assume the recommended choices.
