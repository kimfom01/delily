# 01 — Verified RFC map

**Snapshot:** 2026-11-11. Primary source: the RFC Editor index
(`https://www.rfc-editor.org/rfc-index.txt`, index generated 2026-11-10) and
the IETF Datatracker. Statuses below are the published status of the RFC, not
a claim that every implementation follows it.

**Status legend**

| Mark | Meaning |
|---|---|
| ✅ | Current, use it |
| 🟡 | Experimental, draft, or optional; use with a feature flag |
| ⛔ | Obsolete or superseded; do not build new code against it |
| ℹ️ | Informational / operational guidance, not a wire protocol |

---

## A. Core transport and message format

| RFC | Title | Status | Relationship | Use |
|---|---|---|---|---|
| **5321** | Simple Mail Transfer Protocol | Draft Standard | Obsoletes 2821; updated by 7504 | The SMTP baseline: commands, replies, limits, timeouts, routing, trace fields |
| **5322** | Internet Message Format | Draft Standard | Obsoletes 2822; updated by 6854 | Header/body syntax, folding, addresses, dates, `Received`/`Return-Path` |
| **2045** | MIME Part 1: Format of Internet Message Bodies | Draft Standard | Updated by 2184, 2231, 5335, 6532 | MIME framing, transfer encodings, canonical form |
| **2046** | MIME Part 2: Media Types | Draft Standard | Updated by 2646, 3798, 5147, 6657, 8098 | `Content-Type`, multipart, `message/*` |
| **2047** | MIME Part 3: Header Extensions for Non-ASCII Text | Draft Standard | Updated by 2184, 2231 | Encoded words in headers |
| **2049** | MIME Part 5: Conformance Criteria | Draft Standard | — | Conformance examples; useful test vectors |
| **2231** | MIME Parameter Value and Encoded Word Extensions | Proposed Standard | Updates 2045/2047/2183 | Parameter continuations, charset/language tags |
| **6532** | Internationalized Email Headers | Proposed Standard | Updates 2045 | UTF-8 header values with SMTPUTF8 |
| **6531** | SMTP Extension for Internationalized Email | Proposed Standard | Obsoletes 5336 | `SMTPUTF8`, internationalized addresses |
| **6533** | Internationalized Delivery Status and Disposition Notifications | Proposed Standard | Updates 3461/3464/3798/6522 | UTF-8 in DSNs/MDNs |
| **6857** | Post-Delivery Message Downgrading for Internationalized Email | Proposed Standard | — | Downgrade EAI when a downstream cannot accept it |
| **6858** | Simplified POP and IMAP Downgrading for Internationalized Email | Proposed Standard | Updates 3501 | Access-side downgrade |
| **5890/5891** | IDNA definitions / protocol | Proposed Standard | Obsoletes 3490/3491 | Domain name internationalization |
| **6365** | Terminology Used in Internationalization in the IETF | BCP 166 | — | Vocabulary only |

**Do not build against:** RFC 821, 2821 (SMTP), RFC 822, 2822 (message format),
RFC 5335/5336 (EAI predecessors), RFC 2048 (media type registration; obsoleted
by 4288/4289).

---

## B. SMTP service extensions

| RFC | Extension | Status | Notes |
|---|---|---|---|
| **1870** | `SIZE` | Internet Standard | Advertise a maximum; reject oversized `MAIL FROM` with `552` |
| **6152** | `8BITMIME` | Internet Standard (STD 71) | `BODY=8BITMIME`; do not require 7-bit downgrade |
| **2920** | `PIPELINING` | Internet Standard (STD 60) | Group commands; must still process in order; never pipeline `DATA` content |
| **3030** | `CHUNKING` / `BDAT` | Proposed Standard | `BDAT <n> [LAST]`; binary-safe alternative to `DATA` |
| **4954** | `AUTH` | Proposed Standard | SASL over SMTP; only on submission/authenticated paths |
| **4959** | `SASL-IR` | Proposed Standard | Initial response in `AUTH` command |
| **6531** | `SMTPUTF8` | Proposed Standard | UTF-8 envelope addresses and headers |
| **3461** | `DSN` | Draft Standard | `RET`, `ENVID`, `NOTIFY`, `ORCPT`; DSN generation |
| **3463** | Enhanced status codes | Draft Standard | `x.y.z` codes in replies and DSNs |
| **3464** | DSN message format | Draft Standard | `multipart/report; report-type=delivery-status` |
| **7504** | `521` and `556` reply codes | Proposed Standard | `521` host does not accept mail; `556` domain lacks mail service |
| **3848** | Transmission types | Draft Standard | `with ESMTP`, `with ESMTPS`, `with UTF8SMTP`, etc. in `Received` |
| **9422** | `LIMITS` | Proposed Standard | Server announces operational limits; client adapts |
| **8689** | `Require TLS` | Proposed Standard | `REQUIRETLS` MAIL parameter; do not downgrade |
| **3207** | `STARTTLS` | Proposed Standard | Updated by 7817; reset state after TLS |
| **1985** | `ETRN` | Proposed Standard | Remote queue start; rarely needed, optional |
| **4468** | `BURL` | Proposed Standard | Submission-side URL fetch; high SSRF risk, optional |

**Order of advertisement matters** only for human readability; clients must
not assume order. Advertise only what you implement, and keep the `EHLO`
response under the 512-octet reply-line limit by using continuation lines.

---

## C. Submission and access

| RFC | Title | Status | Relationship | Use |
|---|---|---|---|---|
| **6409** | Message Submission for Mail | Internet Standard (STD 72) | Updated by 8314 | MSA on 587; auth, validation, canonicalization |
| **8314** | Cleartext Considered Obsolete: TLS for Email Submission and Access | Proposed Standard | Updates 1939/2595/3501/5068/6186/6409; updated by 8997 | Implicit TLS for 465/993/995; TLS required for auth |
| **8997** | Deprecation of TLS 1.1 for Email Submission and Access | Proposed Standard | Updates 8314 | TLS 1.1 and below are out |
| **1939** | Post Office Protocol v3 | Internet Standard (STD 53) | — | POP3 if you need it; prefer IMAP/JMAP |
| **2595** | Using TLS with IMAP, POP3 and ACAP | Proposed Standard | Updated by 8314 | Legacy STARTTLS guidance; still relevant for 143 |
| **6186** | Use of SRV Records for Locating Email Submission/Access Services | Proposed Standard | Updated by 8314 | `_submission._tcp` (587), `_imaps._tcp` (993) discovery |
| **5804** | ManageSieve | Proposed Standard | Updated by 7817, 8553 | Remote Sieve script management on 4190 |
| **9051** | IMAP4rev2 | Proposed Standard | Obsoletes 3501 | Modern IMAP baseline |
| **3501** | IMAP4rev1 | Proposed Standard | Obsoleted by 9051, still universally deployed | Implement rev1 compatibility even if you target rev2 |
| **9755** | IMAP Support for UTF-8 | Proposed Standard | Obsoletes 6855 | UTF-8 in IMAP for rev1; rev2 already includes it |
| **9208** | IMAP QUOTA Extension | Proposed Standard | Obsoletes 2087 | Storage/message quotas |
| **7162** | IMAP CONDSTORE/QRESYNC | Proposed Standard | Obsoletes 4551/5162 | Mod-sequence and quick resync |
| **2177** | IMAP IDLE | Proposed Standard | — | Push-like new-mail notification |
| **4315** | IMAP UIDPLUS | Proposed Standard | — | `APPENDUID`, `COPYUID`, `UID EXPUNGE` |
| **5256** | IMAP SORT and THREAD | Proposed Standard | Updated by 5957 | Server-side sorting/threading |
| **4978** | IMAP COMPRESS | Proposed Standard | — | `COMPRESS=DEFLATE` |
| **5161** | IMAP ENABLE | Proposed Standard | — | Enable extensions such as `CONDSTORE`, `UTF8=ACCEPT` |
| **5464** | IMAP METADATA | Proposed Standard | — | Per-mailbox/server metadata |
| **6154** | IMAP SPECIAL-USE | Proposed Standard | — | `\Sent`, `\Drafts`, `\Trash`, `\Junk`, `\Archive` |
| **8474** | IMAP OBJECTID | Proposed Standard | Updates 3501 | Stable object identifiers |
| **8437** | IMAP UNAUTHENTICATE | Proposed Standard | Updates 3501 | Reuse a connection after auth |
| **5530** | IMAP Response Codes | Proposed Standard | — | `[ALERT]`, `[OVERQUOTA]`, `[TOOBIG]`, etc. |
| **2342** | IMAP NAMESPACE | Proposed Standard | Updated by 4466 | Namespace discovery |
| **2971** | IMAP ID | Proposed Standard | — | Client/server identification |
| **5258** | IMAP LIST extensions | Proposed Standard | — | `LIST-EXTENDED`, `LIST-STATUS` |
| **4469** | IMAP CATENATE | Proposed Standard | Updated by 5550 | Build messages on the server |
| **3502** | IMAP MULTIAPPEND | Proposed Standard | Updated by 4466/4469 | Multiple messages in one `APPEND` |
| **3516** | IMAP BINARY | Proposed Standard | Updated by 4466 | Binary literals |
| **7888** | IMAP LITERAL+ | Proposed Standard | Obsoletes 2088 | Non-synchronizing literals |
| **4314** | IMAP ACL | Proposed Standard | Obsoletes 2086 | Shared mailboxes and rights |
| **5182** | IMAP SEARCHRES | Proposed Standard | — | `$` search result reference |
| **4731** | IMAP ESEARCH | Proposed Standard | Updated by 9394 | Efficient search results |
| **5267** | IMAP Contexts | Proposed Standard | Updated by 5465/9394 | Contexts for search/thread |
| **5465** | IMAP NOTIFY | Proposed Standard | Updates 5267 | Server-to-client notifications |
| **9394** | IMAP PARTIAL | Proposed Standard | Updates 4731/5267 | Paged `SEARCH`/`FETCH` |
| **9585** | IMAP Progress Notifications | Proposed Standard | — | `[PROGRESS]` response code |
| **9586** | IMAP UIDONLY | Experimental | — | Suppress sequence numbers |
| **9590** | IMAP METADATA in LIST | Proposed Standard | — | Return metadata in `LIST` |
| **9698** | JMAPACCESS for IMAP | Proposed Standard | — | Tell IMAP clients JMAP is available |
| **9738** | IMAP MESSAGELIMIT | Experimental | — | Announce per-command message limits |
| **10022** | IMAP UIDBATCHES | Proposed Standard | — | UID ranges for batching (useful with UIDONLY) |
| **8970** | IMAP PREVIEW | Proposed Standard | — | Generate message previews |

**Rev1 vs rev2:** RFC 9051 is the current specification, but the installed
base is overwhelmingly rev1. Implement a rev1-compatible command set first,
then add rev2 capabilities and `ENABLE`-gated behavior. Do not claim
`IMAP4rev2` until the rev2-only requirements are actually implemented.

---

## D. JMAP

| RFC | Title | Status | Use |
|---|---|---|---|
| **8620** | JMAP core | Proposed Standard | Session, method calls, state, push |
| **8621** | JMAP for Mail | Proposed Standard | `Mailbox`, `Thread`, `Email`, `EmailSubmission`, `Identity`, `VacationResponse` |
| **9404** | JMAP Blob Management | Proposed Standard | Upload/download blobs; updates 8620 |
| **9425** | JMAP for Quotas | Proposed Standard | Quota data model |
| **9670** | JMAP Sharing | Proposed Standard | Sharing between users; updates 8620 |
| **9610** | JMAP for Contacts | Proposed Standard | Optional; out of initial scope |
| **9661** | JMAP for Sieve Scripts | Proposed Standard | Manage Sieve over JMAP |
| **9698** | JMAPACCESS for IMAP | Proposed Standard | IMAP-to-JMAP migration signal |
| **9749** | VAPID in JMAP Web Push | Proposed Standard | Web Push authentication |
| **8887** | JMAP WebSocket subprotocol | Proposed Standard | Push over WebSocket |
| **9219** | S/MIME signature verification for JMAP | Proposed Standard | Optional |
| **9007** | MDN handling with JMAP | Proposed Standard | Optional |

JMAP is **JSON over HTTPS**, not a replacement for SMTP. It is an access
protocol, like IMAP. Implement it after the storage model and IMAP semantics
are stable; otherwise you will implement the same bugs twice.

### HTTP submission API

| RFC | Title | Status | Use |
|---|---|---|---|
| **9110** | HTTP Semantics | Internet Standard (STD 97) | Methods, status codes, auth, content negotiation |
| **9111** | HTTP Caching | Internet Standard (STD 98) | Cache semantics; do not cache submission responses |
| **9112** | HTTP/1.1 | Internet Standard (STD 99) | Message framing; updated by 9931 |
| **9113** | HTTP/2 | Proposed Standard | Multiplexed transport |
| **9114** | HTTP/3 | Proposed Standard | QUIC transport |
| **9457** | Problem Details for HTTP APIs | Proposed Standard | `application/problem+json` errors; obsoletes 7807 |
| **6750** | OAuth 2.0 Bearer Token Usage | Proposed Standard | `Authorization: Bearer`; updated by 9700 |
| **6749** | OAuth 2.0 Authorization Framework | Proposed Standard | Delegated API access |
| **7519** | JSON Web Token (JWT) | Proposed Standard | Token format |
| **9068** | JWT Profile for OAuth 2.0 Access Tokens | Proposed Standard | Access-token claims/validation |
| **9700** | OAuth 2.0 Security Best Current Practice | BCP 240 | Token/client security |

A custom REST submission API is Delily-specific, but its HTTP behavior and
error model should follow these RFCs. If you want a standards-based HTTP
submission model, use JMAP `EmailSubmission` (RFC 8621) and share the same
queue.

---

## E. Email authentication

| RFC | Title | Status | Relationship | Use |
|---|---|---|---|---|
| **7208** | SPF v1 | Proposed Standard | Obsoletes 4408; updated by 7372/8553/8616 | Authorize sending IPs |
| **6376** | DKIM Signatures | Internet Standard (STD 76) | Obsoletes 4871/5672; updated by 8301/8463/8553/8616 | Cryptographic message signature |
| **8301** | DKIM crypto/key usage update | Proposed Standard | Updates 6376 | RSA-SHA256, key sizes, SHA-1 out |
| **8463** | DKIM Ed25519-SHA256 | Proposed Standard | Updates 6376 | Modern signature algorithm |
| **8616** | Email Authentication for Internationalized Mail | Proposed Standard | Updates 6376/7208/7489 | EAI interaction |
| **9989** | DMARC | Proposed Standard | Obsoletes 7489/9091 | **Current DMARC**; policy, alignment, discovery |
| **9990** | DMARC Aggregate Reporting | Proposed Standard | Obsoletes 7489 | XML aggregate reports |
| **9991** | DMARC Failure Reporting | Proposed Standard | Obsoletes 7489; updates 6591 | Per-message failure reports |
| **8617** | ARC | Experimental | — | Authenticated chain of custody for forwarders |
| **8601** | Authentication-Results header | Proposed Standard | — | Record auth verdicts; strip untrusted copies |
| **6591** | Authentication Failure Reporting using ARF | Proposed Standard | Updated by 9991 | Failure-report transport |
| **6651** | DKIM failure reporting | Proposed Standard | — | `fo=d` detail |
| **6652** | SPF failure reporting | Proposed Standard | — | `fo=s` detail |

**Critical 2026 change:** RFC 9989 replaced RFC 7489. The new record grammar
has `p`, `sp`, **`np`**, `adkim`, `aspf`, `fo`, `rua`, `ruf`, **`psd`**, and
**`t`**. It does **not** define `pct`, `rf`, or `ri`. If you copy an old
DMARC tutorial, you will implement tags that current receivers ignore.

---

## F. Transport security, DNS, and certificates

| RFC | Title | Status | Relationship | Use |
|---|---|---|---|---|
| **9846** | TLS 1.3 | Proposed Standard | Obsoletes 8446/5246/5077/6961/7627/8422; updates 5705/6066 | Current TLS 1.3 spec |
| **9325** | TLS/DTLS recommendations | BCP 195 | Obsoletes 7525; updated by 9852/10015 | Cipher/protocol policy |
| **9852** | New protocols must require TLS 1.3 | BCP 195 | Updates 9325 | New protocol endpoints require 1.3 |
| **10015** | Deprecating obsolete TLS 1.2 key exchange | Proposed Standard | Updates 9325 and many | Remove DHE/ECDHE legacy groups |
| **8996** | Deprecating TLS 1.0/1.1 | BCP 195 | Updates many | Never enable 1.0/1.1 |
| **9525** | Service Identity in TLS | Proposed Standard | Obsoletes 6125 | Certificate name verification rules |
| **5280** | X.509 PKI certificate/CRL profile | Proposed Standard | Updated by many | Certificate parsing/validation |
| **8659** | CAA | Proposed Standard | Obsoletes 6844 | Restrict which CAs may issue |
| **8555** | ACME | Proposed Standard | — | Automated certificate issuance |
| **8737** | ACME TLS-ALPN-01 | Proposed Standard | — | Alternative ACME challenge |
| **8461** | MTA-STS | Proposed Standard | — | Require TLS to MX hosts via HTTPS policy |
| **8460** | SMTP TLS Reporting | Proposed Standard | — | Report TLS failures to domain owner |
| **7672** | SMTP DANE with TLSA | Proposed Standard | — | DNSSEC-anchored TLS authentication |
| **7671** | DANE operational guidance | Proposed Standard | Updates 6698 | TLSA usage/rollover |
| **6698** | DANE TLSA | Proposed Standard | Updated by 7218/7671/8749 | TLSA record semantics |
| **4033/4034/4035** | DNSSEC | Proposed Standard | Updated by many | Authenticated DNS |
| **1035** | DNS implementation/specification | Internet Standard (STD 13) | Updated by many | Resolver basics |
| **2181** | DNS clarifications | Proposed Standard | Updates 1034/1035/1123 | CNAME/MX rules |
| **6891** | EDNS(0) | Internet Standard (STD 75) | — | Larger DNS responses, DNSSEC |
| **8020** | NXDOMAIN | Proposed Standard | Updates 1034/2308 | Do not query below NXDOMAIN |
| **9460** | SVCB/HTTPS RR | Proposed Standard | — | Service binding; useful for autodiscovery |
| **7817** | Updated TLS Server Identity Check Procedure for Email Protocols | Proposed Standard | Updates 2595/3207/3501/5804 | Certificate-name checks for SMTP submission, IMAP, POP, ManageSieve |

> Always verify an RFC title before citing it. The index is the source of
> truth; several email-related RFC numbers are easy to confuse.

---

## G. Notifications, abuse, and operations

| RFC | Title | Status | Use |
|---|---|---|---|
| **3461/3463/3464** | DSN extension / enhanced codes / DSN format | Draft Standard | Delivery status and bounces |
| **8098** | Message Disposition Notification | Internet Standard (STD 85) | Read receipts |
| **3834** | Recommendations for Automatic Responses | Proposed Standard | Vacation/auto-reply rules |
| **2369** | List-* URLs | Proposed Standard | Mailing-list headers |
| **2919** | List-Id | Proposed Standard | List identification |
| **8058** | One-click unsubscribe | Proposed Standard | `List-Unsubscribe-Post` |
| **5965** | Abuse Reporting Format (ARF) | Proposed Standard | Feedback loops, spam reports |
| **6650** | Creation/use of feedback reports | Proposed Standard | ARF updates |
| **6471** | DNSBL operational practices | Informational | How (not) to use blocklists |
| **2142** | Role mailbox names | Proposed Standard | `postmaster`, `abuse`, `hostmaster`, etc. |
| **2033** | LMTP | Informational | Local delivery protocol (optional) |
| **7505** | Null MX | Proposed Standard | Explicitly "this domain accepts no mail" |
| **8601** | Authentication-Results | Proposed Standard | Auth verdict header |
| **5321 §4.5.4** | Retry strategy | Draft Standard | At least 30 min between retries; give up after 4–5 days |
| **5321 §6.3** | Loop detection | Draft Standard | At least 100 `Received` entries |

---

## H. Recently published / changed (read this before copying old guides)

| Date | RFC | Change | Impact |
|---|---|---|---|
| 2026-05 | **9989/9990/9991** | DMARCbis published | `pct`, `rf`, `ri` gone; `np`, `psd`, `t` added |
| 2026-07 | **9846** | TLS 1.3 bis | Obsoletes RFC 8446 and TLS 1.2 RFC 5246 |
| 2026-07 | **9852** | BCP 195 update | New protocols must require TLS 1.3 |
| 2026-07 | **10015** | TLS 1.2 key-exchange deprecations | Remove legacy DHE/ECDHE groups |
| 2026-07 | **10022** | IMAP UIDBATCHES | Batching for large mailboxes / UIDONLY |
| 2025-03 | **9755** | IMAP UTF-8 | Obsoletes 6855; rev2 already includes it |
| 2025-01 | **9698** | JMAPACCESS for IMAP | Migration signal |
| 2024-11 | **9670** | JMAP Sharing | Multi-user mailbox sharing |
| 2024-02 | **9422** | SMTP LIMITS | Servers can announce limits |
| 2023-08 | **9404** | JMAP Blob Management | Large attachment upload/download |
| 2022-03 | **9208** | IMAP QUOTA | Obsoletes 2087 |
| 2021-08 | **9051** | IMAP4rev2 | Obsoletes 3501 |

---

## I. Drafts to watch (not standards)

These are **not** normative as of the snapshot. Track them, but gate any
implementation behind a config flag and a compatibility test.

| Draft | Rev / expiry | Why it matters |
|---|---|---|
| `draft-ietf-emailcore-rfc5321bis` | rev 44, expired 2026-02-02 | SMTPbis: eventual RFC 5321 replacement; do not build to it yet |
| `draft-ietf-emailcore-rfc5322bis` | rev 12, expired 2024-12-15 | Message-format revision; dormant at snapshot |
| `draft-ietf-emailcore-as` | rev 31 | Applicability statement for core email |
| `draft-ietf-mailmaint-smtputf8-syntax` | rev 06 | Tightens SMTPUTF8 address syntax |
| `draft-ietf-mailmaint-interoperable-addresses` | rev 03 | Practical EAI interoperability |
| `draft-ietf-mailmaint-imap-objectid-bis` | rev 08 | OBJECTID update |
| `draft-ietf-mailmaint-oauth-public` | rev 06 | OAuth profile for open public clients |
| `draft-ietf-mailmaint-autoconfig` | rev 07 | Mail AutoConfig / PACC |
| `draft-ietf-mailmaint-expires` | rev 06 | Updated `Expires` header semantics |
| `draft-ietf-mailmaint-aprf` | rev 01 | Aggregate performance reporting |
| `draft-ietf-mailmaint-unobtrusive-signatures` | rev 02 | End-to-end signatures |
| `draft-ietf-mailmaint-wrong-recipient` | rev 05 | Misdirected-email reporting |

**Rule:** an Internet-Draft can change or expire. If a draft feature is not
needed for interoperability, do not ship it. If it is needed, isolate it.

---

## J. Verification log

| Source | Retrieved | Used for |
|---|---|---|
| `https://www.rfc-editor.org/rfc-index.txt` | 2026-11-11 | Status, obsoletes/updates, titles |
| `https://datatracker.ietf.org/api/v1/doc/document/rfc9051/` | 2026-11-11 | IMAP4rev2 metadata |
| `https://www.rfc-editor.org/rfc/rfc5321.txt` | 2026-11-11 | SMTP limits, timeouts, retry, loop detection |
| `https://www.rfc-editor.org/rfc/rfc9989.txt` | 2026-11-11 | Current DMARC tag grammar |
| `https://www.postfix.org/smtp-smuggling.html` | 2026-11-11 | SMTP smuggling mitigation |
| `https://sec-consult.com/blog/detail/smtp-smuggling-spoofing-e-mails-worldwide/` | 2026-11-11 | Smuggling background |
| `https://datatracker.ietf.org/doc/draft-ietf-emailcore-rfc5321bis/` | 2026-11-11 | Draft status |

### Re-verification commands

```bash
# Current index; grep for an RFC number
curl -s https://www.rfc-editor.org/rfc-index.txt | grep -A4 '^5321 '

# Metadata for a specific RFC
curl -s 'https://datatracker.ietf.org/api/v1/doc/document/rfc9989/?format=json' \
  | jq '{title, std_level, rfc, time}'

# A draft's revision/expiry
curl -s 'https://datatracker.ietf.org/api/v1/doc/document/draft-ietf-emailcore-rfc5321bis/?format=json' \
  | jq '{title, rev, expires, rfc}'
```

If a status here disagrees with the RFC Editor index, the index wins. Update
this document and note the date.
