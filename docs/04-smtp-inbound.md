# 04 — SMTP inbound (port 25)

**Normative base:** RFC 5321 (SMTP), RFC 5322 (message format), RFC 1870
(`SIZE`), RFC 6152 (`8BITMIME`), RFC 2920 (`PIPELINING`), RFC 3030
(`CHUNKING`/`BDAT`), RFC 6531 (`SMTPUTF8`), RFC 3461 (`DSN`), RFC 7504
(`521`/`556`), RFC 9422 (`LIMITS`).

This document describes the **receiving MTA** on port 25. Submission on
587/465 is in `07-submission-and-access.md`.

---

## 1. What port 25 is for

Port 25 accepts mail **for domains you host** and, only under explicit
policy, relays mail for trusted peers. It is not an authenticated user
service. It MUST NOT be an open relay.

Decision table:

| Situation | Response |
|---|---|
| `RCPT TO` is a local user/alias/catch-all | Accept (subject to policy) |
| `RCPT TO` is in a domain you relay for, from an authorized peer | Accept |
| Anything else | `550 5.7.1 Relay access denied` |
| Domain is local but user unknown | `550 5.1.1` (or accept then bounce only if you must) |
| Temporary directory failure | `451 4.3.0` |
| Out of storage | `452 4.3.1` |
| Message too large | `552 5.3.4` |

If you accept a message and later cannot deliver it, you must bounce it
**only if the reverse-path is non-null**. Never bounce a bounce.

---

## 2. Session state machine

Model it explicitly. A table-driven transition function is easier to test
than nested `if`s.

```
            ┌─────────────┐
   connect  │  Connected  │
            └──────┬──────┘
                   │ 220 greeting
                   ▼
            ┌─────────────┐   EHLO/HELO    ┌──────────────┐
            │  Greeted    │───────────────▶│  Identified  │
            └─────────────┘                └──────┬───────┘
                                                  │ MAIL FROM
                                                  ▼
                                           ┌──────────────┐
                                           │   MailFrom   │
                                           └──────┬───────┘
                                                  │ RCPT TO (1..n)
                                                  ▼
                                           ┌──────────────┐
                                           │   RcptTo     │
                                           └──────┬───────┘
                                                  │ DATA
                                                  ▼
                                           ┌──────────────┐
                                           │    Data      │
                                           └──────┬───────┘
                                                  │ <CRLF>.<CRLF>
                                                  ▼
                                           ┌──────────────┐
                                           │  Accepted    │──▶ reset to Identified
                                           └──────────────┘
```

Rules:

- `EHLO`/`HELO` is required before `MAIL`.
- `MAIL` requires `Identified`; a second `MAIL` without `RSET` is `503`.
- `RCPT` requires `MailFrom`; `DATA` requires at least one accepted `RCPT`.
- `RSET` clears the envelope and returns to `Identified`; it does not clear
  the HELO identity.
- After a successful `DATA`, clear the envelope but keep the HELO identity.
- `QUIT` is valid in any state and returns `221`.
- `EHLO` after `MAIL` is a sequence error (`503`).
- Unknown commands do not change state; return `500`.
- A fatal framing error closes the connection after the reply.

---

## 3. Command surface

### Required

| Command | Behavior |
|---|---|
| `EHLO <domain>` | Advertise extensions; record identity |
| `HELO <domain>` | Legacy; no extensions; still record identity |
| `MAIL FROM:<path> [params]` | Start transaction; parse reverse-path and ESMTP params |
| `RCPT TO:<path> [params]` | Add recipient; validate local/relay policy |
| `DATA` | `354`, then read until `<CRLF>.<CRLF>` |
| `RSET` | Reset envelope |
| `NOOP` | `250`, no state change |
| `QUIT` | `221`, close |
| `VRFY <string>` | `252` if disabled (recommended); never claim verification you did not do |
| `HELP [string]` | `214` with a short command list |

### Optional / extension-gated

| Command | Gate |
|---|---|
| `STARTTLS` | Advertised only if TLS is configured |
| `AUTH` | **Not on port 25**; submission only |
| `BDAT <n> [LAST]` | Only if `CHUNKING` advertised |
| `BURL` | Only if explicitly enabled; SSRF risk |
| `ETRN` | Only if you run a legacy queue-start service |
| `EXPN` | `502` unless explicitly enabled for authenticated/trusted peers |

### `EHLO` response

Keep each line under 512 octets including CRLF; use continuation lines. A
typical response:

```
250-mx.example.com Hello client.example.com [192.0.2.10]
250-SIZE 52428800
250-8BITMIME
250-PIPELINING
250-ENHANCEDSTATUSCODES
250-SMTPUTF8
250-CHUNKING
250-STARTTLS
250-LIMITS RCPTMAX=100 MAILMAX=50
250 DSN
```

Advertise only what you implement. `LIMITS` parameters should match the
enforced values; see RFC 9422 for the registry syntax. If you advertise
`SMTPUTF8`, you MUST accept UTF-8 addresses and headers, and you SHOULD
advertise `8BITMIME`.

---

## 4. Reply codes

Use the right class and a stable enhanced status code.

| Code | Meaning | Typical use |
|---|---|---|
| `211` | System status | `HELP`/status |
| `214` | Help message | `HELP` |
| `220` | Service ready | Greeting, `STARTTLS` ready |
| `221` | Closing | `QUIT` |
| `250` | OK | Accepted command/message |
| `251` | User not local; will forward | `RCPT` with forwarding |
| `252` | Cannot VRFY, but will accept | Disabled `VRFY` |
| `354` | Start mail input | `DATA` |
| `421` | Service not available, closing | Overload, shutdown, timeout |
| `450` | Mailbox unavailable (temporary) | Greylisting, temporary policy |
| `451` | Local error in processing | DB/DNS/queue failure |
| `452` | Insufficient storage | Disk/quota full |
| `455` | Unable to accommodate parameters | Unsupported parameter combination |
| `500` | Syntax error, command unrecognized | Garbage command |
| `501` | Syntax error in parameters | Bad address/parameter |
| `502` | Command not implemented | `EXPN` disabled |
| `503` | Bad sequence | `DATA` before `RCPT` |
| `504` | Command parameter not implemented | Unknown ESMTP parameter |
| `530` | Authentication required | Submission, not port 25 |
| `538` | Encryption required | Plaintext `AUTH` |
| `550` | Mailbox unavailable | Unknown user, relay denied |
| `551` | User not local; try forward path | Optional forwarding hint |
| `552` | Exceeded storage allocation | Message too large |
| `553` | Mailbox name not allowed | Invalid address syntax |
| `554` | Transaction failed | Policy/content rejection |
| `555` | MAIL/RCPT parameters not recognized | Bad ESMTP params |
| `556` | Domain does not accept mail | Null MX / policy |
| `521` | Host does not accept mail | RFC 7504 |

Enhanced codes to use consistently:

| Situation | Code |
|---|---|
| Other/undefined | `x.0.0` |
| Addressing | `x.1.0`–`x.1.10` |
| Mailbox | `x.2.0`–`x.2.4` |
| Mail system | `x.3.0`–`x.3.5` |
| Network/routing | `x.4.0`–`x.4.7` |
| Mail delivery protocol | `x.5.0`–`x.5.5` |
| Message content | `x.6.0`–`x.6.2` |
| Security/policy | `x.7.0`–`x.7.29` |

Common examples:

```
250 2.1.0 Sender OK
250 2.1.5 Recipient OK
354 End data with <CRLF>.<CRLF>
250 2.0.0 Message accepted for delivery
451 4.3.0 Temporary local error
452 4.3.1 Insufficient system storage
550 5.1.1 No such user
550 5.7.1 Relay access denied
552 5.3.4 Message size exceeds fixed maximum message size
554 5.6.0 Message content rejected
```

---

## 5. Framing and limits

### Line endings

SMTP commands and data lines end with **CRLF** (`\r\n`). A bare LF or bare CR
is not valid framing. The historical practice of accepting bare LF is exactly
what enables SMTP smuggling.

Recommended policy:

| Context | Action |
|---|---|
| Command line contains bare LF | `500 5.5.2` and close (or reject the command) |
| `DATA` body contains bare LF | Normalize to CRLF **or** reject `554 5.6.0`; pick one and document it |
| `DATA` terminator is `<LF>.<LF>` | Never treat as end-of-data |
| `DATA` terminator is `<CRLF>.<CRLF>` | Correct end-of-data |
| Bare CR in command | `500 5.5.2` |

For maximum interoperability, many MTAs normalize bare LF in the body. For
maximum security, reject. If you normalize, do it **before** DKIM
verification and record that you changed the bytes; do not silently produce a
message whose DKIM signature was computed over different bytes.

### Limits

| Object | Minimum/maximum | Source |
|---|---|---|
| Local-part | 64 octets max | RFC 5321 §4.5.3.1.1 |
| Domain | 255 octets max | RFC 5321 §4.5.3.1.2 |
| Reverse/forward path | 256 octets max | RFC 5321 §4.5.3.1.3 |
| Command line | 512 octets including CRLF | RFC 5321 §4.5.3.1.4 |
| Reply line | 512 octets including CRLF | RFC 5321 §4.5.3.1.5 |
| Text line | 1000 octets including CRLF | RFC 5321 §4.5.3.1.6 |
| Message content | at least 64 KiB must be accepted | RFC 5321 §4.5.3.1.7 |
| Recipients | at least 100 must be buffered | RFC 5321 §4.5.3.1.8 |
| `Received` entries | reject at ~100 to stop loops | RFC 5321 §6.3 |

Implement a configurable message max (25–50 MiB is common) and advertise it
with `SIZE`. Reject an oversized `MAIL FROM` with `552 5.3.4` when the client
declares `SIZE`; enforce again while streaming because the client may lie.

### Timeouts

RFC 5321 §4.5.3.2 gives client-side minima. Use them as your defaults:

| Wait | Timeout |
|---|---|
| Initial 220 | 5 min |
| `MAIL` reply | 5 min |
| `RCPT` reply | 5 min |
| `DATA` initiation (354) | 2 min |
| Each data block | 3 min |
| `DATA` termination (250) | 10 min |
| Server waiting for next command | at least 5 min |

Add shorter handshake/TLS timeouts (10–30 s) and a maximum session duration.
On timeout, send `421 4.4.2` if possible, then close.

---

## 6. Address parsing

### Reverse-path (`MAIL FROM`)

```
MAIL FROM:<>
MAIL FROM:<user@example.com>
MAIL FROM:<"john doe"@example.com>
MAIL FROM:<user@[192.0.2.1]>
MAIL FROM:<user@[IPv6:2001:db8::1]>
MAIL FROM:<@relay1,@relay2:user@example.com>
```

- `<>` is the null reverse-path: valid for DSNs and other automatic mail.
  It means "never send a bounce for this message".
- Source routes (`@a,@b:`) are obsolete. You may accept and strip them, or
  reject with `501`; document the choice. Do not use them for routing.
- Domain literals are valid syntax; apply policy (most public servers reject
  them).
- Preserve the local-part case for delivery; compare domains case-insensitively.
- Do not silently truncate an overlong local-part; reject `501 5.1.3`.

### Forward-path (`RCPT TO`)

- Null forward-path is invalid (`501`).
- `postmaster` without a domain is a special case in some deployments; decide
  explicitly and document it.
- Plus addressing (`user+tag@example.com`) is a local convention, not an RFC
  requirement. Implement it only if your user model supports it.
- UTF-8 addresses require `SMTPUTF8`; otherwise reject `553 5.6.7` (or
  `501`).

### ESMTP parameters

| Parameter | Where | Meaning |
|---|---|---|
| `SIZE=n` | `MAIL` | Declared message size |
| `BODY=7BIT` | `MAIL` | 7-bit body |
| `BODY=8BITMIME` | `MAIL` | 8-bit body; requires `8BITMIME` |
| `BODY=BINARYMIME` | `MAIL` | Binary body; requires `CHUNKING` |
| `SMTPUTF8` | `MAIL` | UTF-8 addresses/headers |
| `RET=HDRS|FULL` | `MAIL` | DSN return content |
| `ENVID=...` | `MAIL` | DSN envelope ID |
| `AUTH=...` | `MAIL` | Authorization identity (submission) |
| `NOTIFY=...` | `RCPT` | DSN notification conditions |
| `ORCPT=...` | `RCPT` | Original recipient for DSN |
| `REQUIRETLS` | `MAIL` | No downgrade below TLS |

Unknown parameters: `555 5.5.4` (or `504`). Do not ignore a parameter whose
semantics you do not implement, especially `REQUIRETLS`.

---

## 7. DATA handling

1. On `DATA`, verify at least one accepted recipient; otherwise `503`.
2. Reply `354 End data with <CRLF>.<CRLF>`.
3. Read lines until a line consisting of a single `.`.
4. Remove one leading dot from any line beginning with `..` (dot-stuffing).
5. Enforce the 1000-octet text-line limit (or a configurable larger limit)
   while reading; do not buffer unbounded lines.
6. Enforce the message size limit; if exceeded, consume/discard the rest of
   the DATA safely and reply `552 5.3.4`.
7. Write to a temp blob, compute a hash, fsync, commit metadata, then reply
   `250 2.0.0 Message accepted for delivery`.
8. On any internal failure before commit, reply `451 4.3.0` (or `452` for
   storage) and do not leave a half-message visible.

### Dot-stuffing examples

```
Client sends:        Server stores:
..leading dot   ->   .leading dot
...two dots     ->   ..two dots
.               ->   end of data
```

The terminating dot is only recognized at the beginning of a line, after
CRLF. A line `.` inside the body must be sent as `..`.

### `BDAT`

If you advertise `CHUNKING`, accept `BDAT <n>` followed by exactly `n`
octets, then `BDAT 0 LAST` to finish. `BDAT` is binary-safe and avoids
dot-stuffing. Rules:

- `BDAT` without `CHUNKING` -> `500`/`502`.
- `BDAT` before `MAIL`/`RCPT` -> `503`.
- A short read -> `451`/close.
- `BDAT 0 LAST` completes the message.
- Do not mix `DATA` and `BDAT` in one transaction.

---

## 8. Relay policy and recipient validation

### Local domains

- Exact match on `domains.name` (case-insensitive, IDNA-normalized).
- Users, aliases, catch-all, and plus-addressing resolved in that order.
- Unknown local user: `550 5.1.1` at `RCPT` if the directory is authoritative.
- If you must accept-then-bounce (e.g., because of a backend lookup), you
  create backscatter; prefer rejecting at `RCPT`.

### Relay domains

Only relay for a domain if one of these is true:

- the connecting peer is in an explicit `relay_peers` list (IP/CIDR,
  authenticated TLS client certificate);
- the recipient domain is configured as a relay domain and the peer is
  authorized for it;
- the message is from an authenticated submission session (different port).

Never relay based on the `From:` header. Never relay based on `HELO`.

### Open-relay test cases

```bash
# From an external host, to an external domain: must be 550
swaks --to victim@example.net --server mx.example.com:25

# RCPT with a source route: must not bypass policy
swaks --to '@evil.com:user@example.net' --server mx.example.com:25

# Percent hack / relay tricks: must be rejected
```

---

## 9. TLS on port 25

- `STARTTLS` is optional for inbound mail; advertise it when configured.
- After `STARTTLS`, discard all prior state (including HELO and any envelope)
  and require a new `EHLO`.
- Do not advertise `AUTH` on port 25; if you do, require TLS and a policy
  reason.
- Certificate validation is the **sender's** job for outbound; as a receiver,
  present a valid certificate for the MX hostname.
- If you use MTA-STS/DANE as a sender, the receiver side still just presents
  TLS; the policy is enforced by the sender.

Recommended TLS policy:

- TLS 1.3 preferred, TLS 1.2 allowed for SMTP interoperability.
- No TLS 1.0/1.1 (RFC 8996).
- Strong AEAD cipher suites; no export/RC4/3DES.
- SNI support; default certificate for the primary MX.
- Handshake timeout 10–30 s.

---

## 10. Trace headers

Add exactly one `Received` header per hop, at the top:

```
Received: from client.example.com (client.example.com [192.0.2.10])
        by mx.example.com with ESMTPS id 4XyZ12
        for <user@example.com>;
        Wed, 11 Nov 2026 12:00:00 +0000
```

- `from` is the HELO/EHLO argument, followed by the verified reverse DNS and
  IP in parentheses.
- `by` is your MX hostname.
- `with` is the transmission type: `SMTP`, `ESMTP`, `ESMTPS`, `ESMTPSA`,
  `UTF8SMTP`, `UTF8SMTPS`, `LMTP`, etc. (RFC 3848).
- `id` is your queue/session ID.
- `for` only when there is a single recipient; otherwise omit it to avoid
  leaking Bcc recipients.
- Date is local time with a numeric zone.

At final delivery, add `Return-Path: <envelope-from>` (top) and
`Delivered-To: <recipient>` (one per recipient). Do not add `Return-Path` at
every relay; it is a final-delivery trace field.

---

## 11. SMTP smuggling

### The attack

A sender puts a line ending such as `<LF>.<CRLF>` inside `DATA`. A receiver
that accepts bare LF as a line terminator may treat it as end-of-data,
then interpret the remaining bytes as SMTP commands. If the outbound path
allows the same sequence through, an attacker can smuggle a second message
with a forged envelope.

This is a **framing consistency** bug, not a content bug. The fix is to make
the command parser and the DATA parser agree on exactly one end-of-data
sequence: `<CRLF>.<CRLF>`.

### Required mitigations

1. Reject or normalize bare LF in commands.
2. Never accept `<LF>.<LF>` or `<LF>.<CRLF>` as end-of-data.
3. Dot-stuff **all** outbound message data, including lines that already
   start with a dot.
4. When relaying, do not pass through a message whose body contains a
   bare-LF dot terminator without normalizing or rejecting it.
5. Test with known payloads, including:
   - `\n.\r\n`
   - `\r\n.\n`
   - `\n.\n`
   - a dot line after a bare LF inside a MIME part
6. Ensure `PIPELINING` does not let a client inject commands after `DATA`
   before the `354`.

### Compatibility note

Strict rejection can break old appliances and broken clients. Make it a
config knob with a secure default:

```yaml
smtp:
  data:
    bare_lf: reject   # reject | normalize
```

If you choose `normalize`, document that DKIM signatures over the original
bytes may fail; verify DKIM before normalization or preserve the original
bytes separately.

---

## 12. Resource protection

| Control | Suggested default |
|---|---|
| Max concurrent connections | 200 per node, 20 per IP |
| Max messages per connection | 50 |
| Max recipients per message | 100 |
| Max auth failures (submission) | 5 per 15 min |
| Max invalid commands | 20, then `421` |
| Max message size | 25–50 MiB |
| Max header section | 1 MiB (configurable) |
| Max `Received` headers | 100 |
| Handshake timeout | 30 s |
| Idle timeout | 5 min |
| Session max duration | 1 h |
| Per-IP acceptance rate | configurable token bucket |

On overload, prefer `421 4.7.0` with a short text and close, rather than
accepting and dropping.

---

## 13. Test checklist

- [ ] `EHLO` returns all implemented extensions and no unimplemented ones.
- [ ] `HELO` works; `MAIL` without `EHLO`/`HELO` is `503`.
- [ ] `MAIL` twice without `RSET` is `503`.
- [ ] `RCPT` before `MAIL` is `503`; `DATA` before `RCPT` is `503`.
- [ ] `RSET` clears the envelope but keeps the HELO identity.
- [ ] `QUIT` returns `221` and closes.
- [ ] Null reverse-path accepted; no bounce is generated for it.
- [ ] Oversized command line -> `500 5.5.2`.
- [ ] Oversized message -> `552 5.3.4`.
- [ ] Too many recipients -> `452 4.5.3` (not `552`).
- [ ] Unknown local user -> `550 5.1.1`.
- [ ] External relay -> `550 5.7.1`.
- [ ] Bare-LF DATA terminator does not create a second message.
- [ ] `PIPELINING` commands are processed in order and replies are in order.
- [ ] `BDAT` short/long reads are handled; `BDAT 0 LAST` completes.
- [ ] `STARTTLS` resets state and requires a new `EHLO`.
- [ ] Timeout sends `421` and closes.
- [ ] Message survives a crash after `250`.
- [ ] `Received` header is well-formed and does not leak Bcc.
- [ ] `VRFY` returns `252` when disabled; never false-positive.
- [ ] `EXPN` is `502` unless explicitly enabled.

Use `swaks`, `smtp-sink`, and raw-socket tests. Add a fuzz corpus for command
and DATA framing.
