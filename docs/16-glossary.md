# 16 — Glossary

Terms used across the docs. RFC numbers are pointers, not full definitions.

---

## Roles and protocols

| Term | Meaning |
|---|---|
| **MTA** | Mail Transfer Agent; relays mail between hosts over SMTP |
| **MSA** | Message Submission Agent; authenticated user→queue (RFC 6409) |
| **MDA** | Mail Delivery Agent; delivers to a mailbox, applies Sieve |
| **MUA** | Mail User Agent; the client (Thunderbird, Apple Mail, webmail) |
| **MRA** | Mail Retrieval/Access Agent; IMAP/JMAP/POP3 server |
| **SMTP** | Simple Mail Transfer Protocol (RFC 5321) |
| **ESMTP** | SMTP with service extensions (`EHLO`) |
| **LMTP** | Local Mail Transfer Protocol (RFC 2033) |
| **IMAP** | Internet Message Access Protocol (RFC 9051 rev2, RFC 3501 rev1) |
| **POP3** | Post Office Protocol v3 (RFC 1939) |
| **JMAP** | JSON Meta Application Protocol (RFC 8620/8621) |
| **ManageSieve** | Remote Sieve script management (RFC 5804) |
| **Sieve** | Mail filtering language (RFC 5228) |
| **Submission** | Authenticated SMTP on 587/465 |
| **Implicit TLS** | TLS from the first byte (465, 993, 995) |
| **STARTTLS** | Upgrade a plaintext connection to TLS (RFC 3207) |

---

## HTTP submission API

| Term | Meaning |
|---|---|
| **API key** | Tenant-scoped credential (`dlly_...`) used to authenticate HTTP submission |
| **Scope** | Permission attached to a key/token (`messages:send`, `messages:read`) |
| **Bearer token** | `Authorization: Bearer <token>` credential (RFC 6750) |
| **OAuth 2.0** | Delegated authorization framework (RFC 6749) |
| **JWT** | JSON Web Token; signed claims (RFC 7519/9068) |
| **Idempotency-Key** | Client-supplied key that makes retries return the original result |
| **Problem Details** | `application/problem+json` error object (RFC 9457) |
| **Webhook** | Server-to-tenant HTTPS callback for delivery events |
| **HMAC** | Keyed hash used to sign webhook payloads |
| **SSRF** | Server-side request forgery; block private IPs for webhooks/policy fetches |
| **202 Accepted** | Request queued, not necessarily delivered |

---

## Envelope and message

| Term | Meaning |
|---|---|
| **Envelope** | `MAIL FROM`/`RCPT TO` and ESMTP parameters; not the headers |
| **Reverse-path** | Envelope sender; `<>` is null |
| **Forward-path** | Envelope recipient |
| **Header section** | RFC 5322 fields before the first empty line |
| **Body** | Everything after the first empty line |
| **Trace header** | `Received`, `Return-Path`, `Delivered-To` |
| **`Received`** | One per hop, prepended at the top |
| **`Return-Path`** | Envelope sender, added at final delivery |
| **`Delivered-To`** | Envelope recipient, added at final delivery |
| **`Authentication-Results`** | Auth verdicts for this receiver (RFC 8601) |
| **`Received-SPF`** | SPF verdict header |
| **`Message-ID`** | Globally unique message identifier |
| **`In-Reply-To` / `References`** | Threading headers |
| **`Auto-Submitted`** | Marks automatic mail (RFC 3834) |
| **DSN** | Delivery Status Notification (RFC 3461/3464) |
| **MDN** | Message Disposition Notification (read receipt, RFC 8098) |
| **NDR** | Non-Delivery Report; a bounce |
| **Bounce** | DSN reporting failure |
| **Backscatter** | Bounce sent to an innocent forged sender |
| **Double bounce** | A bounce that itself fails; goes to postmaster |
| **Null sender** | `MAIL FROM:<>`; never bounce it |
| **Open relay** | Relays mail for anyone; must never happen |
| **Relay domain** | A domain you forward mail for by policy |
| **Catch-all** | Accepts unknown local recipients for a domain |
| **Plus addressing** | `user+tag@example.com` sub-addressing convention |

---

## MIME and content

| Term | Meaning |
|---|---|
| **MIME** | Multipurpose Internet Mail Extensions (RFC 2045–2049) |
| **Media type** | `type/subtype`, e.g. `text/plain` |
| **Boundary** | Delimiter for multipart bodies |
| **CTE** | `Content-Transfer-Encoding` (7bit/8bit/binary/QP/base64) |
| **Quoted-printable** | Encoding for mostly-text 8-bit data |
| **Base64** | Binary-to-text encoding |
| **Encoded word** | RFC 2047 encoded header text |
| **RFC 2231** | Parameter continuations for long/non-ASCII values |
| **`message/rfc822`** | A nested message |
| **`message/global`** | UTF-8 nested message (EAI) |
| **`multipart/alternative`** | Same content in increasing fidelity |
| **`multipart/mixed`** | Body + attachments |
| **`multipart/related`** | HTML + inline resources |
| **`multipart/report`** | DSN/MDN container |
| **`multipart/signed`** | S/MIME or PGP signed content |
| **`multipart/encrypted`** | Encrypted content; server should not decrypt |
| **`message/external-body`** | External reference; do not fetch |
| **`message/partial`** | Message fragment; reassemble with limits |
| **Defect** | A recorded parsing problem; not necessarily a rejection |

---

## Internationalization

| Term | Meaning |
|---|---|
| **EAI** | Email Address Internationalization (RFC 6531/6532/6533) |
| **SMTPUTF8** | ESMTP extension for UTF-8 addresses/headers |
| **IDNA** | Internationalized Domain Names in Applications (RFC 5890/5891) |
| **A-label / U-label** | ASCII (`xn--`) / Unicode domain form |
| **Downgrade** | Convert EAI to ASCII for a legacy peer (RFC 6857/6858) |
| **`message/global`** | UTF-8 message type |

---

## Authentication

| Term | Meaning |
|---|---|
| **SPF** | Sender Policy Framework (RFC 7208) |
| **DKIM** | DomainKeys Identified Mail (RFC 6376) |
| **DMARC** | Domain-based Message Authentication, Reporting, and Conformance (RFC 9989) |
| **ARC** | Authenticated Received Chain (RFC 8617, Experimental) |
| **Alignment** | SPF/DKIM domain matches the From domain |
| **Relaxed alignment** | Organizational domains match |
| **Strict alignment** | Exact domain match |
| **Organizational domain** | Registrable domain from the Public Suffix List |
| **PSL** | Public Suffix List |
| **PSD / PSO** | Public Suffix Domain / Operator (RFC 9989) |
| **`p`, `sp`, `np`** | DMARC policies for domain, existing subdomains, non-existent subdomains |
| **`pct`** | Removed in RFC 9989; do not implement |
| **`rua` / `ruf`** | DMARC aggregate / failure report URIs |
| **Aggregate report** | XML summary of DMARC results (RFC 9990) |
| **Failure report** | Per-message DMARC failure report (RFC 9991) |
| **Selector** | DKIM key name, e.g. `selector1` |
| **`_domainkey`** | DNS namespace for DKIM keys |
| **Canonicalization** | Normalization before DKIM hashing (simple/relaxed) |
| **Body hash (`bh`)** | Hash of the canonicalized body |
| **`l=`** | DKIM body length tag; truncation risk |
| **Oversigning** | Listing a header more times than it appears to prevent additions |
| **AUID (`i=`)** | DKIM agent/user identifier |
| **Key rotation** | Publishing a new selector and retiring the old key |
| **Revocation** | Publishing `p=` empty for a DKIM key |
| **`cv=`** | ARC chain validation result (`none`/`pass`/`fail`) |
| **AAR / AMS / AS** | ARC authentication results / message signature / seal |

---

## Transport security

| Term | Meaning |
|---|---|
| **TLS** | Transport Layer Security (RFC 9846 for 1.3) |
| **SNI** | Server Name Indication |
| **OCSP** | Online Certificate Status Protocol |
| **HSTS** | HTTP Strict Transport Security (RFC 6797) |
| **CAA** | Certification Authority Authorization (RFC 8659) |
| **ACME** | Automatic Certificate Management Environment (RFC 8555) |
| **MTA-STS** | SMTP MTA Strict Transport Security (RFC 8461) |
| **TLS-RPT** | SMTP TLS Reporting (RFC 8460) |
| **DANE** | DNS-Based Authentication of Named Entities (RFC 6698/7671/7672) |
| **TLSA** | DANE certificate association record |
| **DNSSEC** | DNS Security Extensions (RFC 4033–4035) |
| **`REQUIRETLS`** | Per-message no-downgrade requirement (RFC 8689) |
| **Certificate chain** | Leaf → intermediates → trusted root |
| **SPKI** | Subject Public Key Info (TLSA selector 1) |
| **STARTTLS stripping** | Attacker removes the STARTTLS capability to force plaintext |

---

## DNS and routing

| Term | Meaning |
|---|---|
| **MX** | Mail exchanger record |
| **Null MX** | `MX 0 .` — domain accepts no mail (RFC 7505) |
| **Implicit MX** | No MX; use the domain’s A/AAAA |
| **PTR** | Reverse DNS record |
| **FCrDNS** | Forward-confirmed reverse DNS |
| **SPF record** | `v=spf1 ...` TXT record |
| **DMARC record** | `v=DMARC1 ...` TXT at `_dmarc` |
| **MTA-STS record** | `v=STSv1; id=...` TXT at `_mta-sts` |
| **TLS-RPT record** | `v=TLSRPTv1; rua=...` TXT at `_smtp._tls` |
| **SRV** | Service location record (RFC 6186 for mail) |
| **EDNS(0)** | DNS extension mechanism (RFC 6891) |
| **NXDOMAIN** | Domain does not exist |
| **CNAME** | Canonical name alias; avoid at MX targets |

---

## Queue and delivery

| Term | Meaning |
|---|---|
| **Queue** | Durable store of accepted-but-undelivered messages |
| **Outbox** | Transactional queue table written with the message |
| **Attempt** | One delivery try to one remote host |
| **Backoff** | Increasing delay between retries |
| **Jitter** | Random variation to avoid synchronized retries |
| **Visibility timeout** | Time before an `inflight` row is reclaimed |
| **Circuit breaker** | Pause a failing domain instead of retrying every message |
| **Give-up time** | When a message becomes a permanent failure (4–5 days typical) |
| **DSN** | Delivery Status Notification |
| **`ENVID` / `ORCPT`** | DSN envelope ID / original recipient |
| **`NOTIFY` / `RET`** | DSN notification conditions / return content |
| **Enhanced status code** | `x.y.z` detail code (RFC 3463) |
| **Loop** | Mail repeatedly returning; detect via `Received`/`Message-ID` |
| **Suppression list** | Addresses that hard-bounced or complained |

---

## IMAP and storage

| Term | Meaning |
|---|---|
| **Mailbox** | Named message container |
| **UID** | Stable per-mailbox message identifier |
| **UIDVALIDITY** | Generation of a mailbox’s UID space |
| **UIDNEXT** | Next UID to assign |
| **Sequence number** | Ephemeral 1..N position in a selected mailbox |
| **MODSEQ** | Modification sequence for flags/changes |
| **HIGHESTMODSEQ** | Current max MODSEQ |
| **`\Recent`** | Session-scoped new-message flag |
| **Keyword** | User-defined flag |
| **`\Deleted`** | Marked for expunge |
| **EXPUNGE** | Permanently remove `\Deleted` messages |
| **ACL** | Access control list for shared mailboxes |
| **Quota** | Storage/message limit (RFC 9208) |
| **Blob** | Immutable raw message bytes |
| **Content-addressed** | Stored under a hash of its content |
| **Sieve** | Server-side filtering language |
| **Thread** | Group of related messages |
| **JMAP state** | Opaque version string for concurrency |

---

## Abuse and reputation

| Term | Meaning |
|---|---|
| **DNSBL** | DNS-based blocklist |
| **URIBL** | Blocklist of domains/URLs in message bodies |
| **Greylisting** | Temporary 4xx for unknown triples |
| **Tarpitting** | Deliberate delay against dictionary attacks |
| **Spam trap** | Address that should never receive legitimate mail |
| **Honeypot** | Decoy address to detect abuse |
| **FBL** | Feedback loop (complaint reporting) |
| **ARF** | Abuse Reporting Format (RFC 5965) |
| **Complaint rate** | Complaints ÷ delivered messages |
| **IP warmup** | Gradual volume ramp on a new IP |
| **Seed list** | Test recipients measuring inbox placement |
| **BIMI** | Brand Indicators for Message Identification |
| **VMC** | Verified Mark Certificate |
| **Joe job** | Forged-sender flood causing bounces/complaints at the victim |

---

## Operations

| Term | Meaning |
|---|---|
| **SLO** | Service level objective |
| **RPO / RTO** | Recovery point / time objective |
| **PITR** | Point-in-time recovery |
| **Scrub** | Verify blobs/metadata integrity |
| **GC** | Garbage collection of unreferenced blobs |
| **Tombstone** | Marker for a deleted object kept for audit |
| **Runbook** | Step-by-step incident procedure |
| **Canary** | Small-scope rollout before full deployment |
| **Blue/green** | Two environments with a traffic switch |
