# 00 — Scope, components, and build/buy decisions

**Goal:** define what Delily is, what it is not (yet), and which parts you
write yourself versus assemble from mature components. This prevents the
classic first-mail-server mistake: implementing MIME parsing from scratch
before you can receive a single message.

---

## 1. The functions behind "mail server"

A complete system is not one daemon. It is at least these functions:

| Function | Standard role | Listens on | Responsibility |
|---|---|---|---|
| **MTA** (Mail Transfer Agent) | SMTP relay/receiver | 25 | Accept mail for local domains, relay outbound, retry, bounce |
| **MSA** (Message Submission Agent) | RFC 6409 | 587, 465 | Authenticate users, validate, canonicalize, hand off to queue |
| **HTTP submission API** | Delily REST (JMAP later) | 443 | Authenticated applications submit messages; same queue as SMTP |
| **MDA** (Mail Delivery Agent) | local delivery | none | Route to mailbox, apply Sieve, quota, spam verdict |
| **MRA / access server** | IMAP/JMAP/POP3 | 143/993/443 | Let clients read/manage mail |
| **Operational plane** | DNS, TLS, monitoring, abuse | 443, DNS | Make the other four trustworthy and deliverable |

The same process can host several roles, but keep the **interfaces** separate:
an inbound SMTP session must not be able to call the IMAP storage layer
directly, and a delivery worker must not depend on an HTTP request.

---

## 2. Scope selector

Build in vertical slices. Pick a target column and do not start the next
column until the previous one is operable.

| Capability | M1: inbound-only | M2: usable mailbox | M3: public sender | M4: modern server |
|---|---|---|---|---|
| SMTP receive (25) | ✅ | ✅ | ✅ | ✅ |
| Local delivery + mailbox store | ✅ | ✅ | ✅ | ✅ |
| IMAP read/write | — | ✅ | ✅ | ✅ |
| Submission (587/465) + AUTH | — | — | ✅ | ✅ |
| HTTP submission API (HTTPS) | — | — | ✅ | ✅ |
| Outbound queue + MX delivery + DSN | — | — | ✅ | ✅ |
| SPF/DKIM/DMARC verification | — | — | ✅ | ✅ |
| DKIM signing + DMARC reporting | — | — | ✅ | ✅ |
| MTA-STS + TLS-RPT + DANE | — | — | — | ✅ |
| ARC verification/sealing | — | — | — | ✅ |
| JMAP + Sieve + quotas/sharing | — | — | — | ✅ |
| Spam/virus pipeline | basic | ✅ | ✅ | ✅ |
| HA, backups, runbooks | — | basic | ✅ | ✅ |

**Recommended first public milestone: M2.** It is the smallest system that is
actually useful and exercises the hardest data-integrity problems (UIDs,
flags, concurrent access) before you add outbound reputation risk.

**Do not expose port 25 to the public Internet until M3's authentication,
abuse controls, and queue/DSN behavior are in place.** An inbound-only server
with no outbound queue can still be abused for backscatter and dictionary
attacks.

---

## 3. Build vs buy: the honest matrix

"From scratch" is a learning goal, not a security strategy. Write the
protocol state machines and policy yourself if that is the point of the
project; use proven primitives for cryptography, TLS, DNS, and storage.

| Piece | Write it yourself? | Recommendation | Why |
|---|---|---|---|
| SMTP state machine | **Yes** | Own code | The core learning goal; small enough to reason about |
| IMAP parser/state machine | **Yes, if learning** | Own code, but expect 3–6 months for a solid rev2 subset | Literal handling, sequence numbers, and concurrency are subtle |
| MIME parser | **No, unless learning** | MimeKit behind `IMimeParser` | Malformed MIME is a security surface; MimeKit is mature and well-tested |
| DKIM/SPF/DMARC algorithms | **Yes** for understanding | Own evaluator; use crypto primitives | The algorithms are the product; do not invent canonicalization |
| RSA/ECDSA/Ed25519 | **No** | `System.Security.Cryptography`; BouncyCastle if a primitive is missing | Never implement crypto |
| TLS | **No** | `SslStream` + OS trust store | Protocol correctness and cipher policy are not a hobby |
| DNS | **No** | `DnsClient.NET` or a validating resolver | DNSSEC validation, EDNS, caching, and timeouts are hard |
| Queue persistence | **Yes** (design) | PostgreSQL transactional outbox | You need to reason about at-least-once delivery |
| Blob storage | **Yes** (design) | Local content-addressed files; S3-compatible later | Simple, debuggable, portable |
| Metadata DB | **No** (engine) | PostgreSQL; SQLite for dev | Transactions, indexes, `tsvector`, row locks |
| Password hashing | **No** | Argon2id (Konscious or libsodium) | Memory-hard, audited |
| Spam filtering | **No** (initially) | rspamd or SpamAssassin over a local socket | Rules and reputation data are a full-time job |
| Virus scanning | **No** | ClamAV over a local socket | Signature feeds and archive handling |
| Web framework | **No** | ASP.NET Core minimal APIs | JMAP/admin/health/HTTP submission endpoints |
| HTTP API contract | **Yes** (design) | OpenAPI + Problem Details | Clients depend on the contract; keep it versioned |
| Observability | **No** | OpenTelemetry + Serilog | You will need traces on day one |

**Decision rule:** if a bug in the component can silently corrupt mail,
lose a message, or leak credentials, prefer the mature component and isolate
it behind an interface. If the component is the thing you want to learn,
write it, but give it a fuzz corpus and a conformance suite.

---

## 4. Recommended .NET shape

Target framework: **net10.0** (the repository already targets it; the
installed SDK is 11.0.100, which can build net10.0).

```
src/
  Delily.Host/                 ASP.NET Core host, DI, config, health, admin API
  Delily.Api/                  HTTP submission contracts + endpoints
  Delily.Protocols.Smtp/       SMTP grammar, session state machine, replies
  Delily.Protocols.Imap/       IMAP grammar, session state machine, responses
  Delily.Protocols.Jmap/       JMAP request/method dispatch (HTTP)
  Delily.Mime/                 RFC 5322/2045 parsing facade, canonicalization
  Delily.Auth/                 SPF, DKIM, DMARC, ARC, Authentication-Results
  Delily.Transport/            MX resolution, TLS policy, SMTP client
  Delily.Queue/                outbox, attempts, retry schedule, DSN generation
  Delily.Storage/              EF Core entities, blob store, migrations
  Delily.Delivery/             routing, Sieve, quota, local delivery
  Delily.Abuse/                DNSBL, greylisting, rate limits, filter pipeline
  Delily.Observability/        metrics, tracing, audit log
tests/
  Delily.UnitTests/
  Delily.IntegrationTests/     Testcontainers + raw TCP clients
  Delily.ConformanceTests/     golden corpus, protocol matrices
  Delily.LoadTests/
```

Keep protocol parsers **pure**: `ReadOnlySequence<byte>` in, state transition
out. Keep policy (is this relay allowed? is this sender authenticated?) in a
separate service. That separation is what makes the edge-case catalog
testable.

### Hosting model

- One `Delily.Host` process can start all listeners as `IHostedService`s for
  development and small deployments.
- For production, allow each listener to be enabled/disabled by config and
  scale the queue/delivery workers independently.
- Use `System.IO.Pipelines` for SMTP/IMAP reads. Avoid `StreamReader` for
  protocol parsing: it hides byte counts, normalizes line endings, and makes
  the 512/1000-octet limits hard to enforce.
- Use `SslStream` **in-process** for SMTP/IMAP TLS. Do not put a generic TCP
  proxy in front of port 25 and assume it preserves the SMTP session.
- Use `Channel<T>` for in-process handoff, but **never** as the only queue:
  a process restart must not lose accepted mail.

---

## 5. Core design principles

1. **Accept responsibility only after durability.** Send `250` for `DATA`
   only after the message and its envelope are committed (fsync/WAL). If you
   cannot commit, return `451`.
2. **Immutable messages, mutable metadata.** Store the exact received bytes
   (plus a normalized copy if needed). IMAP flags, mailbox membership, and
   UIDs live in the database. Never rewrite the blob for a flag change.
3. **At-least-once delivery, idempotent effects.** The queue may retry after
   a crash; delivery must tolerate duplicates. Use stable queue IDs and
   per-mailbox dedupe keys.
4. **Strict on framing, liberal on content.** Reject ambiguous line endings
   and malformed command framing (SMTP smuggling). Be tolerant of historical
   message syntax, but record defects instead of silently repairing.
5. **No open relay, ever.** Port 25 relays only for explicitly configured
   domains/peers; submission relays only for authenticated users.
6. **Fail closed on authentication, fail open on availability?** No: fail
   closed for policy that protects users (TLS verification when MTA-STS/DANE
   says enforce), fail open only for optional reputation signals with a
   logged reason.
7. **Every accepted message has an audit trail.** Queue ID, session ID,
   envelope, authentication results, policy decisions, delivery attempts.
8. **Resource limits are protocol features.** Max line, max size, max
   recipients, max connections, max auth attempts, max IMAP literals.
9. **Separate tenant data by construction.** Every query includes a tenant or
   domain key; cross-tenant IDs return `404`, not `403`.
10. **Make it observable before it is fast.** You cannot debug a mail loop
    without `Received` counts, queue ages, and per-domain delivery metrics.

---

## 6. Non-goals for the first release

- Calendaring (CalDAV/CardDAV), contacts sync, and webmail UI.
- Full-text search at millions of messages (start with PostgreSQL FTS).
- Multi-region active-active delivery.
- A general-purpose spam classification ML pipeline.
- BIMI/VMC production integration (publish the DNS record later; it requires
  a verified mark certificate).
- Being a public relay for third-party domains, including via HTTP.
- A public "send as any domain" API; HTTP submission is tenant-scoped.
- Implementing S/MIME decryption or PGP decryption on the server.

Write these down as explicit non-goals so scope creep does not delay M1/M2.

---

## 7. ADR index

| ADR | Decision |
|---|---|
| [0001](adr/0001-language-and-runtime.md) | .NET 10 / C# as the implementation platform |
| [0002](adr/0002-modular-monolith.md) | Modular monolith with protocol/policy/storage boundaries |
| [0003](adr/0003-storage-engine.md) | PostgreSQL metadata + content-addressed blob store |
| [0004](adr/0004-mime-parser.md) | MimeKit behind a narrow parser interface |
| [0005](adr/0005-durable-queue.md) | PostgreSQL transactional outbox, at-least-once delivery |
| [0006](adr/0006-authentication.md) | Argon2id passwords, SCRAM-SHA-256, OAuth2/OIDC |
| [0007](adr/0007-tls-termination.md) | In-process TLS with OS trust store and ACME certificates |
| [0008](adr/0008-http-submission-api.md) | Authenticated REST + raw MIME submission, shared queue |

Add a new ADR when a decision is expensive to reverse, affects a public
interface, or changes the security model. Do not use ADRs for routine code
style.
