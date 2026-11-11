# 07 — Submission and access

**Normative base:** RFC 6409 (submission), RFC 8314 (TLS for submission and
access), RFC 4954 (SMTP AUTH), RFC 4422 (SASL), RFC 5802/7677 (SCRAM),
RFC 7628 (OAUTHBEARER), RFC 9051 (IMAP4rev2), RFC 3501 (IMAP4rev1), RFC 9755
(IMAP UTF-8), RFC 5804 (ManageSieve), RFC 8620/8621 (JMAP).

This document covers the user-facing protocols. It assumes the storage model
from `10-storage-and-delivery.md` and the authentication primitives from
`08-email-authentication.md`.

---

## 1. Message submission (MSA)

### Role

Submission accepts mail **from authenticated users**, validates it, adds
missing trace fields, and hands it to the outbound queue. It is not a relay
for arbitrary senders.

Applications can use the same submission function over HTTPS. The endpoint
contract, API-key/OAuth model, idempotency, and webhook design are in
[`17-http-api.md`](17-http-api.md). SMTP and HTTP must share one submission
service, DKIM signer, and queue.

### Ports

| Port | TLS | Use |
|---|---|---|
| 587 | STARTTLS (required before AUTH) | Standard submission |
| 465 | Implicit TLS (`submissions`) | Recommended by RFC 8314 |
| 25 | Usually no auth | Inbound MTA, not user submission |

Offer both 587 and 465. RFC 8314 treats cleartext submission as obsolete and
recommends implicit TLS for access/submission; requiring TLS before `AUTH` is
the minimum.

### Differences from port 25

| Behavior | Port 25 | Submission |
|---|---|---|
| Authentication | No | Required |
| Relay | Only local/authorized peers | Authenticated users only |
| Sender authorization | Envelope-based | Must match authenticated identity/aliases |
| Canonicalization | Minimal | Add `Date`, `Message-ID`; fix `From` |
| Rate limits | Per-IP | Per-user and per-domain |
| TLS | Opportunistic | Required for AUTH; implicit TLS preferred |

### Validation (RFC 6409)

- `From:` is required and must be a single mailbox (or have `Sender:`).
- Add `Date:` if missing.
- Add `Message-ID:` if missing; generate a globally unique value.
- Reject a message with no recipients, no `From`, or an invalid `From`.
- Enforce a maximum size and recipient count.
- Do not rewrite the body unless policy requires it; if you do, re-sign DKIM.
- Remove any client-supplied `Return-Path`, `Received`, or
  `Authentication-Results` that claims to be from your system.
- Add your own `Received` header and `Authentication-Results` after
  authentication.

### Sender authorization

The authenticated user may use:

- their primary address;
- configured aliases;
- addresses in domains they own (if multi-domain);
- explicitly delegated send-as addresses.

Anything else: `550 5.7.1 Sender address rejected`. Do not rely on `From:`
alone; check the envelope sender and, if present, the `Sender:`/`From:`
headers against the identity.

---

## 2. SMTP AUTH

### Mechanisms

| Mechanism | Recommendation | Notes |
|---|---|---|
| `SCRAM-SHA-256` | ✅ preferred | Mutual auth, no plaintext password on the wire |
| `SCRAM-SHA-256-PLUS` | ✅ if channel binding available | RFC 9266 `tls-exporter` |
| `PLAIN` | ✅ over TLS | Simple, widely supported |
| `LOGIN` | ⚠️ legacy toggle | Broken base64 challenge sequence; support only for old clients |
| `OAUTHBEARER` | ✅ for OAuth/OIDC | RFC 7628; bearer token, no password |
| `XOAUTH2` | ⚠️ non-standard but common | Gmail-style; support if you need Outlook/Gmail clients |
| `CRAM-MD5` | ⛔ | Obsolete; do not add |

### Rules

- Require TLS before `AUTH` (RFC 8314). Return `538 5.7.11` if not.
- Do not advertise `AUTH` on port 25.
- Support `SASL-IR` (RFC 4959) to save a round trip.
- On failure, return `535 5.7.8` and increment a per-user/IP failure counter.
- After N failures, return `454 4.7.0` temporarily and/or lock the account.
- Never log the password, token, or full SASL exchange.
- Constant-time comparisons for secrets.
- Rehash passwords on successful login if the work factor changed.

### Password storage

Use Argon2id with a per-user salt, a memory cost appropriate for your host
(64–256 MiB), and a time cost calibrated to ~100–300 ms. Store the algorithm,
parameters, salt, and hash in a single encoded string so you can migrate.
Do not use raw SHA-256/MD5, even with a salt.

---

## 3. IMAP

### Which version?

- **IMAP4rev1** (RFC 3501) is the compatibility baseline. Almost every client
  speaks it.
- **IMAP4rev2** (RFC 9051) is the current specification and includes many
  extensions. Claim `IMAP4rev2` only when you implement its requirements.
- A practical plan: implement rev1 + the high-value extensions, then add
  rev2 capabilities and `ENABLE`-gated behavior.

### State machine

```
Not Authenticated
  ├─ LOGIN / AUTHENTICATE ──▶ Authenticated
  ├─ STARTTLS ──────────────▶ Not Authenticated (reset)
  └─ LOGOUT ────────────────▶ Logout

Authenticated
  ├─ SELECT / EXAMINE ──────▶ Selected
  ├─ CREATE / DELETE / RENAME / LIST / STATUS / APPEND
  └─ LOGOUT ────────────────▶ Logout

Selected
  ├─ FETCH / STORE / SEARCH / COPY / MOVE / EXPUNGE
  ├─ CLOSE / UNSELECT ──────▶ Authenticated
  └─ LOGOUT ────────────────▶ Logout
```

### Wire format

- Client commands are tagged: `A001 LOGIN "user" "pass"`.
- Server untagged responses start with `*`.
- Server continuation starts with `+`.
- Tagged completion: `A001 OK ...`, `A001 NO ...`, `A001 BAD ...`.
- Literals: `{n}` means "send n octets after a continuation"; `{n+}` means
  non-synchronizing (LITERAL+), send immediately.
- Strings can be quoted (`"..."`) or literals; NIL is a special atom.
- Case-insensitive command names and system flags; mailbox names are
  case-sensitive except `INBOX`.

### Core commands

| Command | Purpose |
|---|---|
| `CAPABILITY` | List supported extensions |
| `NOOP` | Keepalive; may return untagged updates |
| `LOGOUT` | `* BYE`, then tagged `OK` |
| `STARTTLS` | Upgrade; reset state; require new `CAPABILITY` |
| `LOGIN` | Username/password; disable if `LOGINDISABLED` |
| `AUTHENTICATE` | SASL; `*` cancels |
| `SELECT` / `EXAMINE` | Open mailbox read-write / read-only |
| `CREATE` / `DELETE` / `RENAME` | Mailbox management |
| `SUBSCRIBE` / `UNSUBSCRIBE` / `LSUB` | Subscription list |
| `LIST` | Mailbox listing with patterns |
| `STATUS` | Message counts, UIDNEXT, UIDVALIDITY, UNSEEN |
| `APPEND` | Add a message with flags/date |
| `FETCH` | Retrieve flags, envelope, body sections |
| `STORE` | Change flags/keywords |
| `SEARCH` | Find messages by criteria |
| `COPY` / `MOVE` | Copy/move messages |
| `EXPUNGE` / `UID EXPUNGE` | Remove `\Deleted` messages |
| `CLOSE` / `UNSELECT` | Close mailbox (CLOSE expunges) |
| `CHECK` | Flush/checkpoint (often a no-op) |
| `IDLE` | Wait for changes (`+ idling`, `DONE`) |
| `NAMESPACE` | Personal/shared namespace discovery |
| `QUOTA` / `QUOTAROOT` | Quota reporting |
| `SORT` / `THREAD` | Server-side sort/thread |
| `COMPRESS` | Deflate the connection |
| `ENABLE` | Enable `CONDSTORE`, `QRESYNC`, `UTF8=ACCEPT` |
| `UNAUTHENTICATE` | Return to not-authenticated state |
| `ID` | Client/server identification |

### Sequence numbers vs UIDs

- Sequence numbers are **ephemeral** and change after `EXPUNGE`.
- UIDs are **stable** within a mailbox and `UIDVALIDITY`.
- Store and reason in UIDs internally. Translate to sequence numbers only at
  the protocol edge, under the selected-mailbox lock.
- `UIDNEXT` is the next UID to assign; it must never decrease.
- `UIDVALIDITY` must not change unless the mailbox is deleted/recreated. If
  it changes, clients must discard cached UIDs and resynchronize.
- `MODSEQ` increases on every flag/mailbox change; `HIGHESTMODSEQ` is the
  current value.

### Flags and keywords

System flags: `\Seen`, `\Answered`, `\Flagged`, `\Deleted`, `\Draft`,
`\Recent`.

- `\Recent` is session-scoped: only one session should see a message as
  recent. It is not a persistent flag.
- `\Deleted` requires `EXPUNGE` to remove.
- Keywords are user-defined; advertise `\*` in `PERMANENTFLAGS` if you allow
  them.
- `STORE` with `.SILENT` suppresses the untagged `FETCH` response.

### FETCH sections

| Item | Meaning |
|---|---|
| `FLAGS` | Current flags |
| `UID` | UID |
| `INTERNALDATE` | Server receipt date |
| `RFC822.SIZE` | Message size in octets |
| `ENVELOPE` | Parsed envelope |
| `BODY` | Non-extensible body structure |
| `BODYSTRUCTURE` | Extensible body structure |
| `BODY[<section>]` | Body section, sets `\Seen` |
| `BODY.PEEK[<section>]` | Body section, does not set `\Seen` |
| `BODY[HEADER.FIELDS (FROM TO)]` | Selected headers |
| `BODY[1.MIME]` | MIME headers of part 1 |
| `BODY[1.TEXT]` | Text of part 1 |
| `RFC822` / `RFC822.HEADER` / `RFC822.TEXT` | Legacy aliases |
| `MODSEQ` | Per-message modseq (CONDSTORE) |

`BODY[]` includes the full message (headers + body). `BODY[TEXT]` excludes
headers. `BODY.PEEK[]` is the safe way to read without marking seen.

### Search

Criteria include `ALL`, `ANSWERED`, `DELETED`, `DRAFT`, `FLAGGED`, `NEW`,
`OLD`, `RECENT`, `SEEN`, `UNSEEN`, `UNANSWERED`, `UNDELETED`, `UNDRAFT`,
`UNFLAGGED`, `BCC`, `BODY`, `CC`, `FROM`, `SUBJECT`, `TEXT`, `TO`, `HEADER`,
`BEFORE`, `ON`, `SINCE`, `SENTBEFORE`, `SENTON`, `SENTSINCE`, `LARGER`,
`SMALLER`, `UID`, `KEYWORD`, `UNKEYWORD`, `NOT`, `OR`, and `CHARSET`.

- Dates are date-only, not timestamps.
- `SINCE`/`BEFORE` compare the internal date; `SENTSINCE` compares the
  `Date:` header.
- `CHARSET UTF-8` requires support; otherwise `NO [BADCHARSET]`.
- Search must be bounded; add `MESSAGELIMIT` or paginate with `PARTIAL`.

### Extensions worth implementing

| Extension | Why |
|---|---|
| `IDLE` | New-mail push for clients |
| `UIDPLUS` | `APPENDUID`, `COPYUID`, `UID EXPUNGE` |
| `LITERAL+` | Fewer round trips for large appends |
| `SPECIAL-USE` | Standard Sent/Drafts/Trash/Junk/Archive |
| `NAMESPACE` | Correct folder hierarchy |
| `QUOTA` | User-visible storage limits |
| `ENABLE` | Gate `CONDSTORE`, `QRESYNC`, `UTF8=ACCEPT` |
| `CONDSTORE`/`QRESYNC` | Efficient resync after reconnect |
| `MOVE` | Atomic move (RFC 6851) |
| `SORT`/`THREAD` | Server-side ordering/threading |
| `COMPRESS` | Bandwidth savings |
| `METADATA` | Per-mailbox settings |
| `ACL` | Shared mailboxes |
| `OBJECTID` | Stable identifiers for JMAP migration |
| `MESSAGELIMIT` | Protect against huge commands |
| `UIDBATCHES` | Batch operations on large mailboxes |
| `JMAPACCESS` | Tell clients JMAP is available |

Do not advertise an extension you have not tested with a real client.

### IMAP edge cases

- `SELECT` a nonexistent mailbox: `NO`, not `BAD`.
- `EXAMINE` is read-only; `STORE` must fail.
- `EXPUNGE` during `FETCH`: sequence numbers shift; clients should use UIDs.
  Send untagged `EXPUNGE` in ascending sequence order, adjusted as you go.
- `FETCH` of an expunged message: `NO` or omit; never return stale data.
- `APPEND` with `\Recent`: ignore the flag; the server decides.
- `COPY` to the same mailbox: create new UIDs.
- `MOVE` must be atomic from the client's perspective; use `COPYUID`.
- `STORE` on an expunged message: `NO`.
- `IDLE` while another command is outstanding: protocol error; one command at
  a time except during literals.
- A literal larger than the configured maximum: `NO [TOOBIG]` / `BAD`, and
  do not desynchronize.
- `LOGIN` when `LOGINDISABLED`: `NO`.
- `STARTTLS` after authentication: `BAD`/`NO`; reset only in the
  not-authenticated state.
- Mailbox names with the hierarchy delimiter: create parents as needed or
  return `NO [CANNOT]` per policy.
- UTF-8 mailbox names require `UTF8=ACCEPT` (rev1 + RFC 9755) or rev2.
- `LIST "" ""` returns the hierarchy delimiter, not a mailbox list.
- `INBOX` is case-insensitive; do not create `inbox` as a separate mailbox.
- `CLOSE` expunges `\Deleted`; `UNSELECT` does not.
- `LOGOUT` sends `* BYE` before the tagged `OK`.
- A client disconnect during a literal must not leave a half-applied append.

---

## 4. JMAP

JMAP is a JSON API over HTTPS. It is not a line protocol and does not replace
SMTP. It is a strong choice for a new web/mobile client.

### Session

`GET https://mail.example.com/.well-known/jmap` returns:

```json
{
  "capabilities": {
    "urn:ietf:params:jmap:core": {},
    "urn:ietf:params:jmap:mail": {}
  },
  "accounts": {
    "A1": {
      "name": "user@example.com",
      "isPersonal": true,
      "isReadOnly": false,
      "accountCapabilities": {}
    }
  },
  "primaryAccounts": { "urn:ietf:params:jmap:mail": "A1" },
  "username": "user@example.com",
  "apiUrl": "/jmap",
  "downloadUrl": "/jmap/download/{accountId}/{blobId}/{name}?accept={type}",
  "uploadUrl": "/jmap/upload/{accountId}/",
  "eventSourceUrl": "/jmap/eventsource/?types={types}&closeafter={closeafter}&ping={ping}",
  "state": "..."
}
```

### Request / response

```json
{
  "using": ["urn:ietf:params:jmap:core", "urn:ietf:params:jmap:mail"],
  "methodCalls": [
    ["Mailbox/get", { "accountId": "A1" }, "c1"],
    ["Email/query", {
      "accountId": "A1",
      "filter": { "inMailbox": "M1" },
      "sort": [{ "property": "receivedAt", "isAscending": false }],
      "limit": 50
    }, "c2"],
    ["Email/get", {
      "accountId": "A1",
      "#ids": { "resultOf": "c2", "name": "Email/query", "path": "/ids" }
    }, "c3"]
  ]
}
```

Response:

```json
{
  "methodResponses": [
    ["Mailbox/get", { "accountId": "A1", "state": "...", "list": [] }, "c1"],
    ["Email/query", { "accountId": "A1", "ids": [], "queryState": "..." }, "c2"],
    ["Email/get", { "accountId": "A1", "state": "...", "list": [] }, "c3"]
  ],
  "sessionState": "..."
}
```

### State and concurrency

- State strings are opaque; never parse them.
- `ifInState` on mutations; a mismatch returns `stateMismatch`.
- `createdIds` maps client temporary IDs to server IDs.
- Back-references (`#ids`) avoid a second round trip.
- All mutations are transactional; a failed method does not partially apply.

### Mail methods

`Mailbox/get`, `Mailbox/set`, `Mailbox/query`; `Thread/get`; `Email/query`,
`Email/get`, `Email/set`, `Email/import`, `Email/copy`; `EmailSubmission/set`,
`EmailSubmission/get`; `Identity/get`; `VacationResponse/get/set`;
`SieveScript/get/set` (RFC 9661); `Quota/get` (RFC 9425); blob upload/download
(RFC 9404).

### Push

- SSE: `eventSourceUrl` returns `text/event-stream`.
- WebSocket: RFC 8887.
- Web Push: RFC 9749 VAPID.
- Push notifications are hints; clients must still fetch state.

### Security

- Require TLS; HSTS on the JMAP host.
- Bearer token (OAuth 2.0) or session cookie; if cookies, add CSRF protection
  and `SameSite`.
- CORS allowlist; no wildcard with credentials.
- Rate-limit per account; cap `limit`, blob size, and request body.
- Return `404` for cross-tenant IDs, not `403`.
- Do not expose internal error details.

---

## 5. Sieve and ManageSieve

Sieve (RFC 5228) is the filtering language. ManageSieve (RFC 5804) manages
scripts over TLS on port 4190.

### Minimal useful extension set

| RFC | Extension |
|---|---|
| 5228 | Base language |
| 5229 | Variables |
| 5230 | Vacation |
| 5231 | Relational |
| 5232 | Imap4flags |
| 5233 | Subaddress |
| 5235 | Spamtest/virustest |
| 5429 | Reject/ereject |
| 6609 | Include |
| 3894 | Copy |
| 5703 | MIME |
| 5435 | Notify |
| 8580 | FCC |
| 9042 | Mailbox ID |
| 9122 | IANA action registry |
| 9661 | JMAP Sieve |

### ManageSieve commands

`CAPABILITY`, `STARTTLS`, `AUTHENTICATE`, `LISTSCRIPTS`, `GETSCRIPT`,
`PUTSCRIPT`, `SETACTIVE`, `DELETESCRIPT`, `RENAMESCRIPT`, `CHECKSCRIPT`,
`HAVESPACE`, `NOOP`, `LOGOUT`.

Rules:

- Require TLS before `AUTHENTICATE`.
- Validate the script before storing; reject with a line/column error.
- Enforce a maximum script size and execution budget.
- A script must not be able to read files, execute commands, or make network
  requests.
- Run scripts under a timeout and a memory cap; a pathological regex or
  `include` loop must not hang delivery.

---

## 6. POP3 (optional)

POP3 (RFC 1939) is a simple download-and-delete protocol. It is a poor fit
for multi-device users because it has no folders, flags, or server-side
search. Offer it only if you need legacy clients.

- Ports 110 (STARTTLS) and 995 (implicit TLS).
- `USER`/`PASS` or `AUTH`; require TLS.
- `STAT`, `LIST`, `RETR`, `DELE`, `NOOP`, `RSET`, `QUIT`, `TOP`, `UIDL`.
- Lock the maildrop for the session; `DELE` is committed on `QUIT`.
- `UIDL` must be stable across sessions.
- Do not delete mail on `RETR`; only on `QUIT` after `DELE`.

---

## 7. Autodiscovery

Help clients find you:

| Mechanism | Record/URL |
|---|---|
| SRV | `_submission._tcp.example.com` (587), `_imaps._tcp.example.com` (993) |
| Autoconfig | `https://autoconfig.example.com/mail/config-v1.1.xml` |
| Well-known | `https://example.com/.well-known/autoconfig/mail/config-v1.1.xml` |
| Outlook Autodiscover | `https://autodiscover.example.com/autodiscover/autodiscover.xml` |
| PACC (draft) | Track `draft-ietf-mailmaint-autoconfig` |

RFC 6186 defines the SRV usage; RFC 8314 updates it. Serve autoconfig over
HTTPS with a valid certificate.

---

## 8. Client interoperability matrix

Test at least:

| Client | Protocols | What to verify |
|---|---|---|
| Thunderbird | IMAP, SMTP submission | Folder list, IDLE, move/copy, search, large append |
| Apple Mail | IMAP, SMTP | Special-use folders, push, attachments |
| Outlook (Windows/Mac) | IMAP/POP, SMTP | Auth, Sent/Drafts behavior, IDLE |
| Outlook mobile | IMAP/SMTP or OAuth | OAuth flow, folder sync |
| K-9 Mail / FairEmail | IMAP, SMTP | IDLE, flags, expunge, EAI |
| Gmail app (other account) | IMAP/SMTP | OAuth or app password |
| Web client (JMAP) | JMAP | Query/get/set, blob, push |

Record the exact client version and the failing command. A protocol trace is
worth more than a screenshot.

---

## 9. Access security checklist

- [ ] TLS required for `AUTH`; implicit TLS offered on 465/993.
- [ ] `LOGINDISABLED` advertised before TLS.
- [ ] Auth rate limits and lockout.
- [ ] Passwords hashed with Argon2id; no plaintext logs.
- [ ] SCRAM/PLAIN/OAUTHBEARER tested.
- [ ] Per-user session limits and idle timeouts.
- [ ] IMAP literal and command size limits.
- [ ] Mailbox access checks on every command (no IDOR).
- [ ] Cross-tenant IDs return `404`.
- [ ] JMAP CORS/CSRF configured.
- [ ] HTTP API keys scoped/tenant-bound; no open relay; idempotency enforced.
- [ ] Sieve scripts sandboxed and time-limited.
- [ ] Audit log for auth, mailbox changes, and script changes.
