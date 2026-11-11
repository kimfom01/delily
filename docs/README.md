# Delily knowledge base

**Status:** research snapshot verified **2026-11-11** against the RFC Editor
index, the IETF Datatracker, and primary vendor/standards sources. Mail
standards move; re-check anything marked 🟡 before relying on it.

This directory is the working reference for building Delily, a modern
Internet mail server in .NET. It is deliberately more detailed than a normal
README: it records the standards, the decisions, the phases, the failure modes,
and the tests that turn "I read the RFC" into "I can operate this".

---

## What is in here

| # | Document | Use it when |
|---|---|---|
| 00 | [Scope and decisions](00-scope-and-decisions.md) | Deciding what to build, what to buy, and which stack pieces to use |
| 01 | [Verified RFC map](01-rfc-map.md) | Checking which spec is current, obsolete, or still a draft |
| 02 | [Architecture](02-architecture.md) | Designing processes, storage, queue, and data flow |
| 03 | [Implementation roadmap](03-implementation-roadmap.md) | Starting work; phase-by-phase tasks and exit criteria |
| 04 | [SMTP inbound](04-smtp-inbound.md) | Implementing the receiver on port 25 |
| 05 | [SMTP outbound and queue](05-smtp-outbound-queue.md) | Implementing delivery, retries, and DSNs |
| 06 | [Message format and MIME](06-message-format-mime.md) | Parsing, storing, and safely rendering messages |
| 07 | [Submission and access](07-submission-and-access.md) | MSA, SMTP AUTH, IMAP, JMAP, Sieve, POP3 |
| 08 | [Email authentication](08-email-authentication.md) | SPF, DKIM, DMARC (RFC 9989), ARC |
| 09 | [Transport security](09-transport-security.md) | TLS, MTA-STS, DANE, TLS-RPT, SMTP smuggling |
| 10 | [Storage and delivery](10-storage-and-delivery.md) | Mailbox model, UIDs, flags, quotas, backups |
| 11 | [Abuse, spam, and reputation](11-abuse-spam-reputation.md) | Inbound filtering and outbound deliverability |
| 12 | [Operations](12-operations.md) | DNS, deployment, monitoring, runbooks, compliance |
| 13 | [Testing and conformance](13-testing-and-conformance.md) | Test strategy, tools, interop, fuzzing |
| 14 | [Edge-case catalog](14-edge-case-catalog.md) | Checking expected behavior for awkward inputs |
| 15 | [Reference library](15-reference-library.md) | Reading, implementations to study, tools |
| 16 | [Glossary](16-glossary.md) | Unfamiliar mail terms |
| 17 | [HTTP message submission API](17-http-api.md) | Sending mail from applications over HTTPS |

Supporting material:

- [`adr/`](adr/) — architecture decision records (one decision per file).
- [`checklists/`](checklists/) — phase gate, launch readiness, security review.

---

## Suggested reading order

1. **00** and **01** — agree on scope and confirm the standards baseline.
2. **02** and **03** — understand the shape and the order of work.
3. **04** → **05** → **06** — the transport core.
4. **07** — access protocols only after the message store is stable.
5. **17** — the HTTPS submission API if applications will send mail.
6. **08** and **09** — authentication and transport security, before exposing
   the server to the public Internet.
7. **10**–**12** — make it operable.
8. **13** and **14** — keep it correct.
9. **15** and **16** — reference while implementing.

If you only have one day, read **01**, **03**, **04**, and **14**.

---

## Scope assumption

These documents assume a **full-stack, self-hosted mail system**:

- inbound MTA on port 25;
- message submission on 587/465 **and over authenticated HTTPS** (`/api/v1/messages`);
- local delivery into mailboxes;
- IMAP4rev1/rev2 access, with JMAP as a later phase;
- outbound queue and remote delivery;
- SPF/DKIM/DMARC/ARC, MTA-STS, DANE, TLS-RPT;
- spam/virus handling, quotas, Sieve, and operations.

They also include a **scope selector** in `00-scope-and-decisions.md` so you
can stop at a smaller milestone (for example, inbound-only) without designing
yourself into a corner.

---

## Conventions

- **MUST / SHOULD / MAY** are used in the RFC 2119 / RFC 8174 sense. When this
  document says "must" in lower case, it is local engineering advice, not a
  normative requirement.
- **Verified** means checked against the RFC Editor index and/or the primary
  source on 2026-11-11. It does **not** mean the RFC is free of errata.
- **HTTP APIs** use RFC 9457 Problem Details for errors, bearer tokens
  (RFC 6750), and OAuth 2.0/JWT where delegated access is needed.
- **RFC 9989** is the current DMARC specification (May 2026). It obsoletes
  RFC 7489 and RFC 9091. Most material on the web still describes RFC 7489;
  do not copy its `pct`, `rf`, or `ri` tags into new code.
- **RFC 9846** is the current TLS 1.3 specification (July 2026) and obsoletes
  RFC 8446. **RFC 9852** (BCP 195 update) says new protocols using TLS must
  require TLS 1.3; SMTP is an existing protocol, so keep TLS 1.2 only for
  interoperability, prefer 1.3, and never enable 1.0/1.1.
- **RFC 5321** is still the current SMTP specification. `draft-ietf-emailcore-
  rfc5321bis` is still an Internet-Draft as of this snapshot; do not build
  production behavior on unpublished draft text without a feature flag.
- Ports are the IANA-registered defaults: 25 (SMTP), 587 (submission), 465
  (submissions/implicit TLS), 143 (IMAP), 993 (IMAPS), 110/995 (POP3/POP3S),
  4190 (ManageSieve), 443 (HTTPS/JMAP), 8080 (local admin/API, not public).

---

## Maintenance rules

1. Re-verify the RFC map quarterly, or before any major protocol change.
2. Keep a dated entry in `01-rfc-map.md` whenever a draft becomes an RFC.
3. When an implementation choice changes, add or supersede an ADR; do not
   rewrite history in an accepted ADR.
4. When a production incident reveals a missing edge case, add it to
   `14-edge-case-catalog.md` and, if possible, a regression test.
5. Never store real user mail, credentials, or personal data in test fixtures
   or documentation.

---

## Important disclaimer

This is engineering research, not legal advice. Privacy, retention, lawful
access, abuse handling, and electronic-communications rules vary by
jurisdiction. Have a lawyer review the operating model before accepting
third-party mail.
