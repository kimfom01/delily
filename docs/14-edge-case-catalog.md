# 14 — Edge-case catalog

Use this as a test checklist and a code-review prompt. Each row is a behavior
you should be able to demonstrate. "Policy" means the RFC leaves room for a
local decision; document yours.

Legend: **R** = reject, **D** = defer/retry, **A** = accept, **Q** =
quarantine, **C** = close connection.

---

## A. SMTP connection and commands

| ID | Case | Expected behavior |
|---|---|---|
| A01 | Command line contains bare LF | `500 5.5.2` or close; never treat LF as CRLF |
| A02 | Command line > 512 octets | `500 5.5.2 Line too long` |
| A03 | `EHLO` after `MAIL` | `503 5.5.1 Bad sequence` |
| A04 | Second `MAIL` without `RSET` | `503` |
| A05 | `RCPT` before `MAIL` | `503` |
| A06 | `DATA` before any `RCPT` | `503` |
| A07 | `RSET` after `MAIL`/`RCPT` | Clear envelope; keep HELO identity |
| A08 | `QUIT` in any state | `221`, close |
| A09 | Unknown command | `500 5.5.1`; state unchanged |
| A10 | `VRFY` disabled | `252 2.5.2 Cannot VRFY` (never a false `250`) |
| A11 | `EXPN` disabled | `502 5.5.1` |
| A12 | `HELO` with no argument | `501 5.5.4` |
| A13 | `EHLO` with an invalid domain | `501` or accept with a defect; be consistent |
| A14 | `MAIL FROM:<>` | Accept; null reverse-path |
| A15 | `RCPT TO:<>` | `501 5.1.3` |
| A16 | 101st recipient with a 100 limit | `452 4.5.3` (not `552`) |
| A17 | Message exceeds `SIZE` | `552 5.3.4` |
| A18 | Text line > 1000 octets | `500` or `552`; enforce a limit |
| A19 | Idle > server timeout | `421 4.4.2`, close |
| A20 | Server overloaded | `421 4.7.0`, close |
| A21 | `STARTTLS` after `MAIL` | `503` |
| A22 | `STARTTLS` succeeds | Reset all state; require new `EHLO` |
| A23 | `AUTH` on port 25 | `503`/`530`; not offered |
| A24 | Pipelined message content before `354` | Reject/close; do not parse as data |
| A25 | `BDAT` without `CHUNKING` | `500`/`502` |
| A26 | `BDAT 0 LAST` | Complete message successfully |
| A27 | `BDAT <n>` short read | `451 4.4.2`, close |
| A28 | Line starts with `.` in `DATA` | Un-stuff one dot; store the original |
| A29 | `<LF>.<LF>` inside `DATA` | Not end-of-data; reject/normalize |
| A30 | 100 `Received` headers | `554 5.4.6` loop |
| A31 | Duplicate `Message-ID` | Accept; dedupe per mailbox/window, do not reject globally |
| A32 | Source route in `MAIL FROM` | Strip and accept, or `501`; never route on it |
| A33 | Unknown ESMTP parameter | `555 5.5.4` |
| A34 | Declared `SIZE` > max | `552` at `MAIL` |
| A35 | Client lies about `SIZE` | Enforce while streaming; `552` at end |
| A36 | 8-bit in command without `SMTPUTF8` | `500`/`501` |
| A37 | `SMTPUTF8` address to non-SMTPUTF8 peer | Fail/downgrade; never silently corrupt |
| A38 | Remote replies `251` | Treat as accepted/forwarded |
| A39 | Remote replies `252` | Treat as accepted (cannot verify) |
| A40 | `421` mid-transaction | Close; retry later |

---

## B. Addressing

| ID | Case | Expected behavior |
|---|---|---|
| B01 | `"john doe"@example.com` | Accept; quoted local-part |
| B02 | `user@[192.0.2.1]` | Accept syntactically; policy may reject |
| B03 | `user@[IPv6:2001:db8::1]` | Accept syntactically; policy may reject |
| B04 | Unicode domain | IDNA-normalize for DNS; preserve display |
| B05 | Local-part case | Preserve for delivery; compare per policy |
| B06 | Domain case | Case-insensitive |
| B07 | Local-part > 64 / domain > 255 | `501 5.1.3` |
| B08 | Empty local-part or domain | `501` |
| B09 | `RCPT TO:<Postmaster>` | Policy: accept for local domain or `501`; document |
| B10 | `user+tag@example.com` | Policy: plus-addressing if enabled |
| B11 | Catch-all mailbox | Policy: accept and route; watch abuse |
| B12 | Alias loop | Detect and reject with `554 5.4.6` |
| B13 | Group/list expansion loop | Detect with a depth/visited set |
| B14 | Address with comment `(foo)` | Parse and ignore the comment |
| B15 | Address with CR/LF injection | Reject `501` |
| B16 | Overlong path > 256 | `501 5.1.3` |

---

## C. DATA, MIME, and content

| ID | Case | Expected behavior |
|---|---|---|
| C01 | Missing `Content-Type` | Treat as `text/plain; charset=us-ascii` |
| C02 | Missing multipart boundary | Defect; quarantine or render as text |
| C03 | Multipart depth > 20 | Reject/quarantine safely |
| C04 | Invalid base64 | Defect; do not crash; quarantine |
| C05 | Invalid quoted-printable | Defect; preserve raw |
| C06 | Unknown charset | Fallback to UTF-8/latin-1; mark defect |
| C07 | Encoded-word `Subject` | Decode for display; preserve raw |
| C08 | RFC 2231 filename continuation | Decode correctly |
| C09 | Filename `../../etc/passwd` | Sanitize; store under generated ID |
| C10 | HTML with `<script>` | Sanitize; never execute |
| C11 | Remote tracking image | Block by default |
| C12 | `message/external-body` | Do not fetch; mark unsafe |
| C13 | `multipart/encrypted` | Do not decrypt; store opaque |
| C14 | `message/rfc822` attachment | Render as attachment, not primary body |
| C15 | `message/partial` | Limit/block; do not reassemble unbounded |
| C16 | Zip bomb | Enforce depth/ratio/size; quarantine |
| C17 | EAI message to non-EAI peer | Downgrade or bounce; never corrupt |
| C18 | Empty body | Valid; DKIM empty-body hash |
| C19 | Body only whitespace | Valid; canonicalize correctly |
| C20 | Header CRLF injection | Reject/encode; never emit unescaped |
| C21 | Duplicate `From` | Reject/repair; DMARC needs one domain |
| C22 | Missing `Date` | Add at submission; preserve if present |
| C23 | Invalid `Message-ID` | Add/replace at submission; log |
| C24 | 998-char line | Accept at the limit; >998 reject/wrap |
| C25 | 8-bit body without `8BITMIME` | Downgrade or reject |
| C26 | Header section > 1 MiB | Reject/quarantine |
| C27 | 1000 MIME parts | Reject/quarantine |
| C28 | Attachment with `.exe` | Block or warn per policy |
| C29 | SVG attachment | Treat as active content; do not inline |
| C30 | `Content-Type: text/html` with binary | Sniff/warn; do not execute |

---

## D. Authentication

| ID | Case | Expected behavior |
|---|---|---|
| D01 | No SPF record | `none`; not a fail |
| D02 | Multiple `v=spf1` records | `permerror` |
| D03 | >10 SPF DNS lookups | `permerror` |
| D04 | >2 void lookups | `permerror` |
| D05 | SPF DNS timeout | `temperror`; not cached as permanent |
| D06 | `include` loop | `permerror`, bounded |
| D07 | `redirect` | Evaluate target; counts toward limit |
| D08 | `exp=` on fail | Fetch explanation; do not fail the result |
| D09 | `ptr` mechanism | Evaluate if present; deprecated |
| D10 | Null reverse-path + HELO | Evaluate HELO identity |
| D11 | No DKIM signature | `none`; DMARC may still pass via SPF |
| D12 | DKIM body tampered | `fail` |
| D13 | DKIM key revoked (`p=`) | `fail` |
| D14 | DKIM RSA key < 1024 | `fail` |
| D15 | DKIM `x=` expired | `fail` |
| D16 | DKIM `l=` truncation | Verify signed prefix; treat tail as untrusted |
| D17 | Duplicate `From` + `h=from` | Canonicalize all/empty per spec; test |
| D18 | DKIM `i=` outside `d=` | `fail` |
| D19 | DKIM DNS TXT split into strings | Concatenate; verify |
| D20 | Ed25519 signature | Verify if supported; otherwise `neutral`/`permerror` |
| D21 | No DMARC record | `none`; no policy |
| D22 | DMARC fail + `p=reject` | Reject at SMTP or discard |
| D23 | Relaxed alignment, org domain match | Pass |
| D24 | Strict alignment, subdomain mismatch | Fail |
| D25 | `sp` for existing subdomain | Apply `sp` |
| D26 | `np` for non-existent subdomain | Apply `np` |
| D27 | Old `pct=` tag | Ignore (not in RFC 9989) |
| D28 | `t=y` | Apply one level lower than policy |
| D29 | External `rua` | Verify `_report._dmarc` before sending |
| D30 | Multiple `From` domains | DMARC fail / reject |
| D31 | ARC `cv=fail` | Do not trust the chain |
| D32 | ARC tampered AMS | Chain fail |
| D33 | ARC `i` gap/duplicate | Chain fail; do not guess |
| D34 | ARC chain > 50 | Fail; bound processing |
| D35 | Forwarded mail: SPF fail, DKIM pass | DMARC can pass via DKIM |
| D36 | Mailing list rewrites Subject | DKIM may fail; ARC may preserve |
| D37 | Authentication-Results from client | Strip/namespace; never trust |

---

## E. Queue, delivery, and DSN

| ID | Case | Expected behavior |
|---|---|---|
| E01 | MX NXDOMAIN | Permanent `5.1.2` |
| E02 | `MX 0 .` null MX | Permanent `5.1.2`/`5.4.4`; no retry |
| E03 | No MX, A/AAAA exists | Implicit MX |
| E04 | MX target is CNAME | Tolerate/log; RFC says avoid |
| E05 | MX points to self | Detect loop; local delivery or fail |
| E06 | Remote `450` | Retry with backoff |
| E07 | Remote `550` | Permanent; bounce |
| E08 | Remote `421` | Retry |
| E09 | Remote `552` | Permanent; bounce |
| E10 | Connection drops after `DATA` | Retry may duplicate; record ambiguity |
| E11 | Worker crashes mid-attempt | Row returns to `queued` after visibility timeout |
| E12 | Null-sender message fails | No DSN; log/double-bounce to postmaster |
| E13 | Bounce of a bounce | Send to `postmaster`, not null sender |
| E14 | `NOTIFY=NEVER` | No DSN |
| E15 | `RET=HDRS` | DSN includes headers only |
| E16 | `ENVID`/`ORCPT` | Echo in DSN |
| E17 | Delayed delivery | One `Action: delayed` DSN, not per retry |
| E18 | Queue expires after 4–5 days | Final bounce `4.4.7`/`5.4.7` |
| E19 | Domain circuit breaker | Pause domain, not each message |
| E20 | MTA-STS enforce + TLS failure | No downgrade; defer/bounce |
| E21 | DANE mismatch | Hard fail; no downgrade |
| E22 | `REQUIRETLS` + no TLS | Permanent failure |
| E23 | SMTPUTF8 unavailable | Downgrade or bounce |
| E24 | 8BITMIME unavailable | Re-encode or bounce |
| E25 | Pipelined replies out of order | Read in order; never assume |
| E26 | Remote closes after `MAIL` | Retry later, not permanent |

---

## F. IMAP

| ID | Case | Expected behavior |
|---|---|---|
| F01 | `SELECT` nonexistent mailbox | `NO` |
| F02 | `STORE` in `EXAMINE` | `NO` (read-only) |
| F03 | `UIDVALIDITY` changes | Client must resync; server must not change silently |
| F04 | `UIDNEXT` decreases | Impossible; bug |
| F05 | `EXPUNGE` shifts sequence numbers | Send untagged `EXPUNGE` correctly; use UIDs |
| F06 | `FETCH` expunged message | `NO`/omit; no stale data |
| F07 | `APPEND` with `\Recent` | Ignore client flag |
| F08 | `COPY` to same mailbox | New UIDs |
| F09 | `MOVE` | Atomic; `COPYUID`; no partial |
| F10 | `STORE` on expunged message | `NO` |
| F11 | Command during `IDLE` | `BAD`; only `DONE` |
| F12 | Literal > max | `NO [TOOBIG]`; no desync |
| F13 | `LOGIN` when `LOGINDISABLED` | `NO` |
| F14 | `STARTTLS` after auth | `BAD`/`NO` |
| F15 | `LIST "" ""` | Return hierarchy delimiter |
| F16 | `INBOX` vs `inbox` | Same mailbox |
| F17 | UTF-8 mailbox without `UTF8=ACCEPT` | `NO` |
| F18 | `CLOSE` vs `UNSELECT` | `CLOSE` expunges; `UNSELECT` does not |
| F19 | `LOGOUT` | `* BYE`, then tagged `OK` |
| F20 | Disconnect mid-literal | No half append; clean session |
| F21 | `BODY[]` vs `BODY[TEXT]` | Full message vs body only |
| F22 | `BODY.PEEK[]` | Does not set `\Seen` |
| F23 | `SEARCH CHARSET` unsupported | `NO [BADCHARSET]` |
| F24 | Quota exceeded | `NO [OVERQUOTA]` |
| F25 | Concurrent flag stores | Last writer wins or conflict; no corruption |
| F26 | Delete mailbox with children | `NO` or cascade per policy |
| F27 | Rename `INBOX` | Special semantics; preserve messages |
| F28 | Sequence number 0 | `BAD` |
| F29 | UID 0 | `BAD` |
| F30 | `SEARCH *` / invalid criterion | `BAD` |
| F31 | `FETCH 1:* (FLAGS UID)` | All messages, correct UIDs |
| F32 | Mailbox name with delimiter | Create parents or `NO`; document |
| F33 | `APPEND` to selected mailbox | Allowed; send `EXISTS` update |
| F34 | Expunge during another session | Notify; do not corrupt sequence state |
| F35 | `NOOP` with pending changes | May send untagged updates |

---

## G. JMAP

| ID | Case | Expected behavior |
|---|---|---|
| G01 | `ifInState` mismatch | `stateMismatch` |
| G02 | Unknown capability in `using` | `unknownCapability` |
| G03 | Unknown method | `unknownMethod` error |
| G04 | Invalid back-reference | `invalidArguments` |
| G05 | `createdIds` reuse | Map only within request |
| G06 | Cross-account ID | `notFound`, not `forbidden` |
| G07 | Blob too large | `tooLarge` |
| G08 | Push disconnects | Client reconnects with state |
| G09 | Rate limit | `rateLimit` with retry hint |
| G10 | Invalid JSON | `invalidArguments`; do not 500 |
| G11 | `Email/set` partial failure | Per-object errors; no partial commit |
| G12 | `EmailSubmission` with bad identity | `invalidArguments`/`forbidden` |

---

## H. TLS, DNS, and operations

| ID | Case | Expected behavior |
|---|---|---|
| H01 | TLS 1.0/1.1 offered | Reject |
| H02 | MTA-STS enforce + cert mismatch | No downgrade |
| H03 | DANE TLSA mismatch | Hard fail |
| H04 | MTA-STS policy 3xx | Do not follow |
| H05 | MTA-STS fetch fails, no cache | Treat as no policy; log |
| H06 | MTA-STS fetch fails, valid cache | Apply cache |
| H07 | TLS-RPT report generated | Valid JSON; sent to `rua` |
| H08 | CAA forbids issuer | Do not issue; alert |
| H09 | SMTP smuggling payload | One message, not two |
| H10 | MTA-STS host resolves to private IP | Block SSRF |
| H11 | DNSSEC bogus | Treat as insecure/fail per policy; never silently trust |
| H12 | Cert expires in 7 days | Alert/page |
| H13 | PTR missing/mismatch | Deliverability risk; alert |
| H14 | MX backup used | Delivery succeeds |
| H15 | DNS timeout | Defer, do not bounce |

---

## I. Storage and integrity

| ID | Case | Expected behavior |
|---|---|---|
| I01 | Blob missing for a membership | Corrupt; alert; do not serve |
| I02 | Orphan blob | GC after grace period |
| I03 | Quota usage drifts | Nightly recompute; correct |
| I04 | Two mailboxes share a blob | Deleting one does not delete blob |
| I05 | Crash during `APPEND` | No half message; temp cleaned |
| I06 | DB failover | UIDs/UIDVALIDITY intact |
| I07 | Search crosses tenant | Never return another tenant's message |
| I08 | Import preserves flags/date | Yes; report failures |
| I09 | Restore from backup | Scrub passes; sample fetch works |
| I10 | Legal hold | Deletion blocked; audit |

---

## J. Abuse and deliverability

| ID | Case | Expected behavior |
|---|---|---|
| J01 | `GTUBE` | Quarantine |
| J02 | `EICAR` | Block/quarantine |
| J03 | DNSBL timeout | Do not block |
| J04 | Greylisted triple retries | Accept after delay |
| J05 | Backscatter attempt | Reject at SMTP; no bounce |
| J06 | Compromised account | Rate-limit/suspend; alert |
| J07 | One-click unsubscribe | Honor immediately |
| J08 | FBL complaint | Suppress recipient |
| J09 | Blocklist listing | Alert + runbook |
| J10 | Joe job | Do not bounce unauthenticated mail |
| J11 | Forwarded spam | Apply user/domain policy; no backscatter |
| J12 | Password-protected archive | Quarantine; cannot scan |

---

## K. HTTP submission API

| ID | Case | Expected behavior |
|---|---|---|
| K01 | Valid JSON request + key | `202`, durable queue row, message ID |
| K02 | Missing/invalid/expired key | `401`; no message accepted |
| K03 | Key for tenant A sends as tenant B | `403`; no cross-tenant access |
| K04 | `From` domain not authorized for key | `403` |
| K05 | No `to`/`cc`/`bcc` recipients | `422` |
| K06 | Body/attachment exceeds limit | `413`, no partial accept |
| K07 | CRLF in display name/header/filename | `400`; reject injection |
| K08 | Caller supplies `Received`/`Return-Path`/`DKIM-Signature` | Reject or strip; never trust |
| K09 | Invalid base64 attachment | `400`; no partial message |
| K10 | Filename `../../etc/passwd` | Sanitize; store under generated ID |
| K11 | Same idempotency key + same body | Same message ID; one queue row |
| K12 | Same idempotency key + different body | `409` |
| K13 | Queue/storage unavailable | `503`; never `202` |
| K14 | Status lookup for another tenant | `404` |
| K15 | Rate limit exceeded | `429` + `Retry-After` |
| K16 | Webhook URL resolves to private IP | Block; log; no request |
| K17 | Webhook signature/replay | HMAC verifies; stale timestamp rejected |
| K18 | Raw MIME forged `Return-Path` | Envelope from headers is authoritative; strip |
| K19 | Raw MIME DKIM from another domain | Verify, then re-sign per tenant policy |
| K20 | OpenAPI schema drifts from implementation | CI contract test fails |
| K21 | `Bcc` present in request | Envelope-only; remove header before delivery |
| K22 | HTML only, no text | Accept; generate a text fallback if policy requires |
| K23 | Attachment content-type mismatch | Store as declared, scan, warn; never execute |
| K24 | `Idempotency-Key` absent while required | `400`/`428` with a clear problem detail |
