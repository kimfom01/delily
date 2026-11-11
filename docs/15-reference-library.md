# 15 — Reference library

A curated starting point. Prefer primary sources; blog posts are useful for
operational color, not for protocol truth.

---

## 1. Primary sources

| Source | Why |
|---|---|
| [RFC Editor](https://www.rfc-editor.org/) | Normative text; use the index to check status |
| [IETF Datatracker](https://datatracker.ietf.org/) | Draft status, WG discussions, errata |
| [IANA Mail Parameters](https://www.iana.org/assignments/mail-parameters) | SMTP extensions, encodings, media types |
| [IANA Message Headers](https://www.iana.org/assignments/message-headers) | Header field registry |
| [IANA TLS Parameters](https://www.iana.org/assignments/tls-parameters) | Cipher suites, extensions |
| [DMARC.org](https://dmarc.org/) | Deployment guidance (check RFC 9989 changes) |
| [MAAWG](https://www.m3aawg.org/) | Operational best practices, abuse handling |
| [CA/Browser Forum](https://cabforum.org/) | Certificate rules |
| [NIST SP 800-52](https://csrc.nist.gov/) | TLS guidance |
| [Google Postmaster Tools](https://postmaster.google.com/) | Reputation/deliverability |
| [Microsoft SNDS](https://sendersupport.olc.protection.outlook.com/snds/) | IP reputation |
| [Spamhaus](https://www.spamhaus.org/) | Blocklist and policy |
| [Postfix SMTP smuggling](https://www.postfix.org/smtp-smuggling.html) | Framing mitigation |

---

## 2. Reading path

### Week 1 — transport

1. RFC 5321 §1–4 (commands, replies, state machine).
2. RFC 5321 §4.5.3 (limits/timeouts), §4.5.4 (retry), §6.3 (loops).
3. RFC 5322 §2–3 (headers, folding, addresses, dates).
4. RFC 6409 (submission).
5. RFC 8314 (TLS for submission/access).

### Week 2 — message content

1. RFC 2045/2046 (MIME framing, media types).
2. RFC 2047/2231 (encoded words, parameters).
3. RFC 6532 (internationalized headers).
4. RFC 3464 (DSN format).
5. Your parser’s fuzz corpus.

### Week 3 — access

1. RFC 9051 (IMAP4rev2) — read the command/response grammar carefully.
2. RFC 3501 (rev1) — for compatibility behavior.
3. RFC 2177/4315/4551/7162/5256/6154 (IDLE, UIDPLUS, CONDSTORE, SORT,
   SPECIAL-USE).
4. RFC 8620/8621 (JMAP) if you are building a modern client API.
5. RFC 5804/5228 (ManageSieve).

### Week 4 — trust

1. RFC 7208 (SPF).
2. RFC 6376 + 8301 + 8463 (DKIM).
3. RFC 9989/9990/9991 (DMARC, current).
4. RFC 8617 (ARC).
5. RFC 8461/8460/7672 (MTA-STS, TLS-RPT, DANE).

### Week 5 — operations

1. RFC 6471 (DNSBL practices).
2. RFC 5965 (ARF), RFC 8058 (one-click unsubscribe).
3. RFC 2142 (role addresses), RFC 3834 (auto-responses).
4. MAAWG sender best practices.
5. Your own runbooks.

---

## 3. Books

| Book | Why |
|---|---|
| *Postfix: The Definitive Guide* — Kyle D. Dent | Queue architecture, relay policy, operational patterns |
| *Exim: The Mail Transfer Agent* — Philip Hazel | ACLs, routing, retry logic |
| *Managing IMAP* — Dianna Mullet & Kevin Mullet | IMAP concepts and client behavior |
| *The Book of IMAP* — Peer Heinlein & Peer Hartleben | Dovecot/Cyrus-era IMAP operations |
| *Programming Internet Email* — David Wood | MIME, SMTP, POP3/IMAP from a developer’s view |
| *Internet Email Protocols: A Developer’s Guide* — Kevin Johnson | Protocol walkthroughs |
| *Bulletproof SSL and TLS* — Ivan Ristić | TLS/PKI depth |
| *DNS and BIND* — Cricket Liu & Paul Albitz | DNS fundamentals |
| *Designing Data-Intensive Applications* — Martin Kleppmann | Queue/storage trade-offs |
| *Site Reliability Engineering* — Google | SLOs, incident response |

Check editions; mail standards changed after many of these were published.
Use the books for architecture, the RFCs for behavior.

---

## 4. Implementations to study

| Project | Language | What to learn |
|---|---|---|
| **Postfix** | C | Queue manager, SMTP client/server, clean separation, safety |
| **Exim** | C | ACL configuration, routing, retry |
| **Dovecot** | C | IMAP correctness, mailbox indexing, auth |
| **Cyrus IMAP** | C | Large-scale mailboxes, ACLs, replication |
| **OpenSMTPD** | C | Readable SMTP state machine (check maintenance status) |
| **Haraka** | JavaScript | Plugin architecture, SMTP hooks |
| **Stalwart** | Rust | All-in-one SMTP/IMAP/JMAP, modern design |
| **WildDuck** | JavaScript | IMAP/JMAP data model |
| **Maddy** | Go | Small, readable mail server |
| **docker-mailserver / mailcow / Mailu** | Shell/Compose | Integration, DNS, TLS, operations |
| **Postal** | Ruby | Outbound delivery/queue |
| **Thunderbird / K-9 Mail** | C++/Kotlin | Real client behavior; test against them |

Read the code for **state machines and failure handling**, not to copy
licensing or architecture wholesale.

---

## 5. .NET libraries

| Library | Use | Notes |
|---|---|---|
| **MimeKit** | MIME parsing/writing | Mature; use behind an interface |
| **MailKit** | SMTP/IMAP/POP client | Useful for tests and migration, not the server |
| **DnsClient.NET** | DNS resolver | Supports EDNS, DNSSEC validation options |
| **Npgsql / EF Core** | PostgreSQL | Use Dapper for hot paths if needed |
| **Konscious.Security.Cryptography** | Argon2id | Or libsodium |
| **BouncyCastle** | Missing crypto primitives | Use only if the runtime lacks what you need |
| **Polly** | Retries/circuit breakers | For delivery workers |
| **Serilog** | Structured logs | JSON sink |
| **OpenTelemetry .NET** | Traces/metrics | Instrument SMTP/IMAP/queue |
| **Testcontainers** | Integration tests | Postgres, Mailpit |
| **FsCheck / CsCheck** | Property tests | Parsers, invariants |
| **SharpFuzz** | Fuzzing | libFuzzer bindings |
| **BenchmarkDotNet** | Microbenchmarks | Parsers, canonicalization |
| **System.IO.Pipelines** | Protocol I/O | Backpressure, zero-copy |
| **System.Threading.Channels** | In-process handoff | Not a durable queue |

---

## 6. Tools

### Protocol

- `swaks` — SMTP/submission Swiss army knife.
- `smtp-sink`, `smtp-source` — Postfix test tools.
- `imaptest` — IMAP conformance/load.
- JMAP Test Suite — JMAP method tests.
- `openssl s_client` — STARTTLS/cert inspection.
- `curl` + `jq` — JMAP/HTTP.
- `tcpdump` — wire truth.

### Authentication

- `dkimpy`, `opendkim-testmsg` — DKIM.
- `spfquery`, `pyspf` — SPF.
- `checkdmarc` — DMARC/DNS.
- OpenARC — ARC.
- `mail-tester`, `port25 verifier`, `dmarcian` — external checks.

### TLS/DNS

- `testssl.sh`, `sslyze` — TLS configuration.
- `dig`, `kdig`, `delv`, `danetest` — DNS/DNSSEC/DANE.
- `internet.nl`, `Hardenize` — external posture checks.

### Abuse

- `rspamd`, `SpamAssassin` — content filtering.
- ClamAV — virus scanning.
- Spamhaus/Barracuda lookup tools.
- Google Postmaster Tools, Microsoft SNDS, Yahoo FBL.

### Load/chaos

- `k6` — HTTP/JMAP load.
- `tc`, `iptables`, Toxiproxy — network faults.
- `faketime` — clock skew tests.
- `BenchmarkDotNet` — microbenchmarks.

---

## 7. Communities

| Community | Why |
|---|---|
| [mailop](https://mailop.org/) | Mail operators; deliverability and abuse |
| Postfix users list | Practical MTA behavior |
| Dovecot list | IMAP/storage behavior |
| Exim users list | Routing/ACL patterns |
| IETF emailcore / mailmaint / dmarc / uta | Standards work |
| MAAWG | Industry best practices |
| Spamhaus | Blocklist operations |
| DMARC.org | Deployment community |

Read the archives before asking; many edge cases have been discussed for
decades.

---

## 8. Lab setup

A minimal lab:

```yaml
services:
  postgres:
    image: postgres:17
    environment:
      POSTGRES_PASSWORD: dev
  mailpit:
    image: axllent/mailpit
    ports: ["1025:1025", "8025:8025"]
  rspamd:
    image: rspamd/rspamd
  clamav:
    image: clamav/clamav
```

Test against:

- your server (SMTP/IMAP/JMAP);
- Postfix as a peer MTA;
- Dovecot as an IMAP peer for migration;
- Mailpit for capture;
- Thunderbird/K-9 for real-client behavior;
- Gmail/Yahoo/Outlook for external auth/deliverability.

---

## 9. What to read next when stuck

| Problem | Read |
|---|---|
| Reply code/sequence | RFC 5321 §4.2–4.3 |
| Message won’t parse | RFC 5322 §2–3, RFC 2045–2046 |
| DKIM fails | RFC 6376 §3.4, §3.7; check canonicalization |
| DMARC alignment | RFC 9989 §4.4 |
| IMAP client desync | RFC 9051 command/response grammar; check literals |
| Queue loops | RFC 5321 §6.3; inspect `Received` |
| TLS failure | RFC 9846, RFC 9525; `openssl s_client` |
| MTA-STS | RFC 8461 §3.3/§4 |
| DANE | RFC 7672 §3 |
| Spam/deliverability | MAAWG, Google/Yahoo sender docs |
| Smuggling | Postfix SMTP smuggling page; SEC Consult paper |
