# 05 — SMTP outbound, queue, and DSNs

**Normative base:** RFC 5321 (SMTP), RFC 1870 (`SIZE`), RFC 6152
(`8BITMIME`), RFC 2920 (`PIPELINING`), RFC 3030 (`CHUNKING`/`BDAT`), RFC 6531
(`SMTPUTF8`), RFC 3461/3463/3464 (DSN), RFC 7505 (null MX), RFC 8461
(MTA-STS), RFC 7672 (DANE), RFC 8689 (`REQUIRETLS`).

This is the sending half: submission hands off a message, the queue owns it
until it is delivered or bounced.

---

## 1. Queue invariants

1. A message is in the queue **before** the submission server returns `250`.
2. One queue row per recipient (or per identical recipient group).
3. A queue row is never lost because a worker crashed.
4. Every attempt is recorded with a timestamp, remote host, response code,
   and diagnostic text.
5. A permanent failure produces exactly one DSN (unless suppressed).
6. A null-sender message never produces a DSN.
7. Retries are bounded; no message retries forever.
8. Delivery is at-least-once; duplicates are possible after a crash.

---

## 2. Queue schema

```sql
CREATE TABLE queue (
  id              uuid PRIMARY KEY,
  message_id      uuid NOT NULL REFERENCES messages(id),
  envelope_from   text NOT NULL,          -- '' for null sender
  recipient       text NOT NULL,
  status          text NOT NULL,          -- queued|inflight|deferred|delivered|bounced|failed
  attempts        int  NOT NULL DEFAULT 0,
  next_attempt_at timestamptz NOT NULL,
  last_attempt_at timestamptz,
  last_error      text,
  remote_mta      text,
  dsn_ret         text,                   -- HDRS|FULL
  dsn_envid       text,
  dsn_notify      text,                   -- NEVER|SUCCESS|FAILURE|DELAY
  dsn_orcpt       text,
  require_tls     boolean NOT NULL DEFAULT false,
  created_at      timestamptz NOT NULL DEFAULT now(),
  claimed_at      timestamptz,
  claimed_by      text,
  finished_at     timestamptz
);

CREATE INDEX queue_ready_idx ON queue (status, next_attempt_at);
CREATE INDEX queue_recipient_idx ON queue (recipient);
```

Attempt history:

```sql
CREATE TABLE delivery_attempts (
  id           bigserial PRIMARY KEY,
  queue_id     uuid NOT NULL REFERENCES queue(id),
  attempted_at timestamptz NOT NULL,
  remote_mta   text,
  remote_ip    inet,
  tls          text,
  response     text,
  enhanced     text,
  diagnostic   text,
  outcome      text                      -- success|defer|fail
);
```

Claim with `FOR UPDATE SKIP LOCKED`:

```sql
WITH claimed AS (
  SELECT id FROM queue
  WHERE status IN ('queued', 'deferred')
    AND next_attempt_at <= now()
  ORDER BY next_attempt_at
  FOR UPDATE SKIP LOCKED
  LIMIT 50
)
UPDATE queue q
SET status = 'inflight', claimed_at = now(), claimed_by = $1
FROM claimed c
WHERE q.id = c.id
RETURNING q.*;
```

A worker that dies leaves rows `inflight`; a sweeper returns rows whose
`claimed_at` is older than the visibility timeout to `queued`.

---

## 3. Routing

### Normalize the domain

1. Trim whitespace, strip a trailing dot, lowercase.
2. IDNA-normalize (RFC 5890/5891) for DNS; preserve the original for the
   envelope.
3. Reject empty, overlong (>255), or syntactically invalid domains.

### Resolve

| DNS result | Action |
|---|---|
| `NXDOMAIN` | Permanent failure `5.1.2` (bad destination domain) |
| `NODATA` for MX, A/AAAA exists | Implicit MX: deliver to the host itself |
| `NODATA` for MX and no A/AAAA | Permanent failure `5.4.4` (no route) |
| `MX 0 .` (null MX, RFC 7505) | Permanent failure `5.1.2`/`5.4.4`; never retry |
| MX records | Sort by preference; randomize equal preferences |
| CNAME at MX target | Tolerate but log; RFC 2181 says MX targets should not be CNAMEs |
| Temporary DNS failure | Defer `4.4.3`; retry with backoff |

Try all MX hosts in preference order (at least two addresses if available).
Do not loop back to yourself; if an MX resolves to your own host, treat it as
a local delivery or fail with a loop diagnostic.

### Address literals

`user@[192.0.2.1]` and `user@[IPv6:2001:db8::1]` bypass MX lookup. Apply
policy: most public MTAs reject them, but they are valid syntax.

---

## 4. Connection and TLS policy

### Plain SMTP

1. TCP connect (Happy Eyeballs for dual-stack).
2. Read `220` greeting (timeout 5 min; use 30–60 s in practice).
3. Send `EHLO <your fqdn>`; parse capabilities.
4. If `STARTTLS` is advertised and policy allows, send `STARTTLS`, read
   `220`, wrap the socket, then send `EHLO` again.
5. Send `MAIL FROM`, `RCPT TO`, `DATA` or `BDAT`.
6. `QUIT` after the transaction (or reuse the connection for more messages
   to the same host).

### Policy decision

| Policy | Behavior on TLS failure |
|---|---|
| Opportunistic (default) | Try TLS; if handshake/cert fails, log and continue in plaintext (unless `REQUIRETLS`) |
| MTA-STS `testing` | Try TLS; on failure, continue but report |
| MTA-STS `enforce` | Do **not** downgrade; defer/bounce |
| DANE (valid TLSA) | Do **not** downgrade; a mismatch is a hard failure |
| `REQUIRETLS` | Do **not** downgrade under any circumstance |

Certificate validation: chain to a trusted root, check the MX hostname
(RFC 9525), check expiry, and use SNI. Do not disable validation to "make it
work".

TLS versions: 1.3 preferred; 1.2 allowed for SMTP interoperability; 1.0/1.1
disabled. Cipher policy: AEAD suites, forward secrecy, no export/RC4/3DES.

### MTA-STS

- Fetch `https://mta-sts.<domain>/.well-known/mta-sts.txt` over HTTPS with a
  valid certificate for `mta-sts.<domain>`.
- Check the `_mta-sts.<domain>` TXT record: `v=STSv1; id=<id>`.
- Policy fields: `version: STSv1`, `mode: enforce|testing|none`,
  `mx: <pattern>` (one or more), `max_age: <seconds>`.
- Cache for `max_age` (cap at 31557600 seconds; typical 86400–604800).
- Match the MX host against the `mx` patterns, including `*.example.com`
  wildcards.
- If the policy is `enforce` and no valid policy can be fetched but a cached
  policy exists and is still valid, use it. If it has expired, treat the
  domain as having no policy (log) or defer depending on your risk posture;
  document the choice.
- MTA-STS does not authenticate the certificate independently of the Web PKI;
  DANE is stronger when both are available.

### DANE

- Require DNSSEC validation (AD bit or local validation).
- Look up `_25._tcp.<mx-host>` TLSA.
- Typical record: `3 1 1 <sha256-of-SPKI>` (DANE-EE, SPKI, SHA-256).
- If a TLSA record exists and does not match, fail; never downgrade.
- If no TLSA record exists, DANE does not apply; fall back to the normal
  policy.
- Support multiple TLSA records for rollover; try each matching record.

---

## 5. SMTP client behavior

### Capability parsing

`EHLO` replies are multi-line:

```
250-mx.example.com Hello
250-SIZE 35882577
250-8BITMIME
250-STARTTLS
250-PIPELINING
250-SMTPUTF8
250 CHUNKING
```

Parse `KEYWORD [params]` case-insensitively. Do not assume order. Handle
unknown keywords by ignoring them.

### `MAIL FROM`

```
MAIL FROM:<sender@example.com> SIZE=12345 BODY=8BITMIME SMTPUTF8
```

Add DSN parameters only if the remote advertises `DSN`:

```
MAIL FROM:<sender@example.com> RET=HDRS ENVID=...
RCPT TO:<recipient@example.org> NOTIFY=FAILURE,DELAY ORCPT=rfc822;...
```

### `RCPT TO`

- One command per recipient.
- Handle `250`/`251` as accepted, `4xx` as temporary, `5xx` as permanent.
- Some servers accept then bounce; you cannot know at `RCPT` time.

### `DATA` vs `BDAT`

- If `CHUNKING` is advertised, `BDAT` is usually faster and binary-safe.
- If not, dot-stuff every line and end with `<CRLF>.<CRLF>`.
- Do not pipeline message content before `354`.
- After the final dot, wait for `250`; if the connection dies, the message may
  have been accepted. Retrying can duplicate it. Record the ambiguity.

### Pipelining

If `PIPELINING` is advertised, you may send a group:

```
MAIL FROM:<...>\r\n
RCPT TO:<...>\r\n
DATA\r\n
```

Read replies in order. Do not send message content until `354`. If any
`RCPT` fails, still read all replies before deciding; a `DATA` after a failed
`RCPT` may be rejected.

### SMTPUTF8 and 8BITMIME

- If the envelope contains non-ASCII and the remote does not advertise
  `SMTPUTF8`, you must downgrade (if possible) or fail permanently.
- If the body is 8-bit and the remote does not advertise `8BITMIME`, you must
  re-encode to 7-bit (quoted-printable/base64) or fail. Re-encoding changes
  the bytes; DKIM signatures must be recomputed if you re-sign.
- Do not send `BODY=BINARYMIME` unless `CHUNKING` is advertised.

---

## 6. Retry schedule

Use exponential backoff with jitter. A practical default:

| Attempt | Delay after failure |
|---|---|
| 1 | immediate |
| 2 | 5 min |
| 3 | 15 min |
| 4 | 30 min |
| 5 | 1 h |
| 6 | 2 h |
| 7 | 4 h |
| 8 | 8 h |
| 9+ | 12 h |

Give up after **4–5 days** for normal mail; 1 day for DSNs and other
automatic mail. RFC 5321 says the retry interval SHOULD be at least 30
minutes and the give-up time generally at least 4–5 days; it also suggests
two attempts in the first hour, then backing off to every 2–3 hours.

Add jitter of ±20% so a restart does not retry every message at once. Keep a
per-domain circuit breaker: after N consecutive connection failures, back off
the whole domain, not each message independently.

### Classify responses

| Response | Class | Action |
|---|---|---|
| `250`, `251` | success | Mark delivered |
| `4xx` | transient | Retry with backoff |
| `5xx` | permanent | Bounce (with exceptions below) |
| `421` | transient | Retry; connection closes |
| `450`, `451`, `452`, `455` | transient | Retry |
| `550 5.1.1` | permanent | Unknown user; bounce and suppress |
| `550 5.7.1` | permanent | Policy/relay denied; bounce (do not retry) |
| `552 5.3.4` | permanent | Too large; bounce |
| `553 5.1.3` | permanent | Bad address; bounce |
| `554 5.7.1` | permanent | Policy; bounce |
| `5.4.4` | permanent | No route; bounce |
| `5.4.6` | permanent | Loop; bounce |
| `5.4.7` | permanent | Too many hops; bounce |
| `4.4.7` | transient | Queue expired; eventually bounce |
| `5.7.0` | permanent | Security policy; bounce |
| `530`, `538` | permanent | Auth/config error; alert, do not retry blindly |

A `5xx` to `MAIL`/`RCPT` can be a temporary policy; use the enhanced code and
your own policy to decide. Never retry a `5xx` for days "just in case" — it
creates backscatter and delays the bounce.

---

## 7. DSN generation

A DSN is a `multipart/report` with `report-type=delivery-status` (RFC 3464):

```
From: Mail Delivery System <MAILER-DAEMON@mx.example.com>
To: <sender@example.net>
Subject: Undelivered Mail Returned to Sender
Date: ...
Message-ID: <...>
Auto-Submitted: auto-replied
MIME-Version: 1.0
Content-Type: multipart/report; report-type=delivery-status;
    boundary="dsn-boundary"

--dsn-boundary
Content-Type: text/plain; charset=us-ascii

Your message could not be delivered to one or more recipients.
...
--dsn-boundary
Content-Type: message/delivery-status

Reporting-MTA: dns; mx.example.com
Arrival-Date: Wed, 11 Nov 2026 12:00:00 +0000

Final-Recipient: rfc822; user@example.org
Original-Recipient: rfc822; user@example.org
Action: failed
Status: 5.1.1
Diagnostic-Code: smtp; 550 5.1.1 No such user
Remote-MTA: dns; mx.example.org
Last-Attempt-Date: Wed, 11 Nov 2026 12:05:00 +0000

--dsn-boundary
Content-Type: message/rfc822

<original message headers (and body if RET=FULL)>
--dsn-boundary--
```

Rules:

- Envelope sender is `<>`; never generate a DSN for a null-sender message.
- `Action`: `failed`, `delayed`, `relayed`, or `expanded`.
- `Status`: enhanced code from RFC 3463.
- `Diagnostic-Code`: `smtp; <remote reply>` or `X-...;` for local.
- `Remote-MTA`: the MX hostname.
- `Original-Recipient` from `ORCPT` if present; otherwise the envelope
  recipient.
- `Original-Envelope-Id` from `ENVID`.
- Honor `NOTIFY=NEVER` (no DSN) and `NOTIFY=SUCCESS` (success DSN, if you
  support it).
- `RET=HDRS` includes headers only; `RET=FULL` includes the full message.
- Suppress DSNs for messages rejected as spam/virus at SMTP time; if you
  accepted them and later classify them, do not bounce (backscatter).
- A bounce of a bounce goes to `postmaster`, never to the null sender.

### Delayed DSN

Send one `Action: delayed` notice after the first few hours (for example, 4 h)
if the message is still queued and the sender requested `NOTIFY=DELAY` or the
default includes delay. Do not send one per retry.

---

## 8. Bounce processing (inbound DSNs)

When you receive a DSN:

1. Parse `message/delivery-status` parts.
2. Extract `Final-Recipient`, `Action`, `Status`, `Diagnostic-Code`,
   `Original-Envelope-Id`.
3. Correlate with the original queue row via `ENVID`/`Message-ID` if possible.
4. On `failed`, suppress the recipient after a threshold (hard bounce).
5. On `delayed`, do nothing except metrics.
6. On `5.1.1`, suppress immediately for that domain/user.
7. Never auto-reply to a DSN.

Soft vs hard bounce policy is yours; document it. Do not suppress on a single
`4xx`.

---

## 9. Loop prevention

- Count `Received` headers; reject at ~100 (RFC 5321 §6.3).
- If the message already has your host in a `Received` `by` clause and is
  arriving again, suspect a loop.
- Honor `Auto-Submitted: auto-replied` / `auto-generated`; never auto-reply
  to those.
- Check `X-Loop` headers if you emit them.
- A message with a `Return-Path: <>` must not generate a bounce.
- Do not forward a message back to the same recipient list without a loop
  breaker.
- For mailing lists, add `List-Id`, `List-Unsubscribe`, and a `Precedence`
  convention; do not rely on `Precedence` for security.

---

## 10. Backscatter

Backscatter is a bounce sent to an innocent sender who did not send the
message (because the envelope sender was forged). Avoid it:

- Reject unknown recipients at `RCPT`, not after `DATA`.
- Reject spam/virus at SMTP time when possible; if you must accept, quarantine
  silently.
- Never bounce a message with a null reverse-path.
- Do not send DSNs for messages that fail DMARC and are likely forged; use
  your policy.
- Rate-limit DSNs per sender domain.

---

## 11. Queue operations

Provide admin commands/API:

| Operation | Behavior |
|---|---|
| `queue list --status deferred` | Inspect rows, age, attempts, last error |
| `queue show <id>` | Message metadata, attempts, DSN params |
| `queue retry <id>` | Reset `next_attempt_at` to now |
| `queue hold <id>` | Pause a row |
| `queue release <id>` | Resume |
| `queue delete <id>` | Delete only with explicit confirmation; audit |
| `queue pause-domain <domain>` | Circuit breaker |
| `queue drain` | Attempt all ready rows once |
| `queue stats` | Depth by status/domain/age |

Never delete a queue row that has been accepted but not delivered without an
audit trail and a policy decision.

---

## 12. Test matrix

- [ ] Local delivery to a Postfix/Mailpit sink succeeds.
- [ ] `EHLO` capabilities are parsed; unknown keywords ignored.
- [ ] `STARTTLS` is used when advertised; state resets.
- [ ] MTA-STS `enforce` prevents plaintext fallback.
- [ ] DANE TLSA mismatch fails; no downgrade.
- [ ] `REQUIRETLS` prevents downgrade.
- [ ] `SIZE` rejection (`552`) is permanent, not retried.
- [ ] `450` retries with backoff; `550` bounces.
- [ ] A connection drop after `DATA` does not lose the queue row.
- [ ] A crash mid-attempt returns the row to `queued`.
- [ ] A DSN has a null reverse-path and valid `message/delivery-status`.
- [ ] A null-sender message never produces a DSN.
- [ ] A 100-`Received` loop is rejected.
- [ ] A null MX produces a permanent failure, not a retry loop.
- [ ] Per-domain concurrency and circuit breaker work.
- [ ] `SMTPUTF8` fallback/downgrade behaves as documented.
- [ ] `BDAT` short read is handled.
- [ ] Pipelined replies are read in order.
