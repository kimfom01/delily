# 06 — Message format and MIME

**Normative base:** RFC 5322 (Internet Message Format), RFC 2045–2049 (MIME),
RFC 2231 (parameter continuations), RFC 2047 (encoded words), RFC 6532
(internationalized headers), RFC 6533 (EAI notifications), RFC 6857/6858
(downgrade), RFC 5321 §4.4 (`Received`).

This document is about **bytes on the wire**, parsing them safely, preserving
them for DKIM, and turning them into something a client can read.

---

## 1. The two layers

A mail message has:

1. **Envelope** — `MAIL FROM`, `RCPT TO`, and ESMTP parameters. It is not part
   of the message content. It controls routing and bounces.
2. **Content** — RFC 5322 headers + body, possibly MIME-structured. It is what
   the user sees and what DKIM signs.

Never derive routing from `From:`/`To:` headers. Never assume the envelope
sender equals the `From:` address. Never assume `To:` lists all recipients
(Bcc exists).

---

## 2. RFC 5322 essentials

### Lines and limits

- Lines end with CRLF.
- A line MUST NOT exceed 998 octets excluding CRLF; SHOULD be ≤78 for
  readability.
- The body may contain any US-ASCII except NUL; 8-bit requires `8BITMIME` or
  `SMTPUTF8`.
- Header section ends at the first empty line.

### Header fields

```
field-name ":" [ field-body ] CRLF
```

- Field names are case-insensitive, printable US-ASCII except colon.
- Folding: a CRLF followed by WSP (space/tab) continues the field. Unfolding
  replaces CRLF+WSP with a single space (or preserves the WSP; be consistent
  for DKIM).
- Duplicate fields are generally allowed except where specified (`From`,
  `Sender`, `Reply-To`, `Date`, `Message-ID` should be unique; `Received` is
  intentionally repeated).
- Obsolete syntax exists (comments, source routes, weird whitespace). Parse
  leniently but record defects.

### Common fields

| Field | Notes |
|---|---|
| `Date` | RFC 5322 date-time; add at submission if missing |
| `From` | Required; mailbox-list; must be a single author for DMARC |
| `Sender` | Required if `From` has multiple mailboxes |
| `Reply-To` | Optional; may differ from `From` |
| `To`, `Cc`, `Bcc` | Destination; `Bcc` must be removed before delivery |
| `Message-ID` | `<local@domain>`; add at submission if missing; do not reuse |
| `In-Reply-To`, `References` | Threading; `References` is a list of Message-IDs |
| `Subject` | Unstructured; may be encoded |
| `Received` | Trace; prepend one per hop |
| `Return-Path` | Final delivery; envelope sender |
| `Delivered-To` | Final delivery; envelope recipient |
| `Authentication-Results` | Auth verdicts; strip untrusted inbound copies |
| `Received-SPF` | SPF verdict (optional but useful) |
| `ARC-*` | ARC chain (see `08-email-authentication.md`) |
| `Auto-Submitted` | `no`, `auto-generated`, `auto-replied` |
| `List-*` | Mailing-list semantics (RFC 2369/2919/8058) |
| `Resent-*` | Resent blocks; handle as a separate header block |

### Address syntax

```
mailbox      = name-addr / addr-spec
name-addr    = [display-name] "<" addr-spec ">"
addr-spec    = local-part "@" domain
local-part   = dot-atom / quoted-string / obs-local-part
domain       = dot-atom / domain-literal / obs-domain
```

Examples that must parse:

```
user@example.com
"john doe"@example.com
John Doe <user@example.com>
"John Doe" <user@example.com>
user@[192.0.2.1]
user@[IPv6:2001:db8::1]
Group: a@example.com, b@example.com;
undisclosed-recipients:;
```

Reject or repair:

- missing `@` (unless a local alias convention),
- control characters,
- CR/LF injection inside a display name or parameter,
- overlong local-part/domain.

### Date parsing

Accept the RFC 5322 forms and common obsolete zones. Normalize to UTC for
storage; preserve the original for display and DKIM. Do not reject a message
solely because the date is implausible; record a defect.

---

## 3. MIME structure

### Top-level

```
MIME-Version: 1.0
Content-Type: text/plain; charset="utf-8"
Content-Transfer-Encoding: quoted-printable
```

Defaults if `Content-Type` is missing: `text/plain; charset=us-ascii`.
`MIME-Version` is required for MIME messages but many messages omit it; be
tolerant.

### Media types

| Type | Meaning | Handling |
|---|---|---|
| `text/plain` | Plain text | Decode charset; render safely |
| `text/html` | HTML | Sanitize; block remote content by default |
| `multipart/*` | Container | Parse boundary; recurse with depth limit |
| `message/rfc822` | Nested message | Parse as a message; do not execute |
| `message/global` | UTF-8 message | EAI equivalent of `message/rfc822` |
| `message/partial` | Fragment | Reassemble only with limits; often malicious |
| `message/external-body` | External reference | Do **not** fetch; treat as unsafe |
| `application/*` | Attachment | Store; scan; never execute |
| `multipart/signed` | S/MIME or PGP signature | Verify optionally; do not strip |
| `multipart/encrypted` | Encrypted | Do not decrypt; store opaque |

### Multipart

```
Content-Type: multipart/mixed; boundary="abc123"

--abc123
Content-Type: text/plain

hello
--abc123
Content-Type: application/pdf
Content-Disposition: attachment; filename="doc.pdf"

...
--abc123--
```

Rules:

- Boundary is required; 1–70 characters, not ending in space.
- The first boundary may be preceded by a preamble; the last is followed by
  an epilogue. Ignore both.
- Nested multipart is allowed; enforce a maximum depth (for example 20) and a
  maximum part count (for example 1000).
- Missing or malformed boundary: treat as a parse defect, quarantine or
  render as text; do not loop forever.
- `multipart/alternative` parts are ordered by increasing fidelity; the last
  supported part is preferred.
- `multipart/related` is used for HTML + inline images (`Content-ID`).
- `multipart/report` is used for DSNs/MDNs; `report-type` matters.

### Transfer encodings

| CTE | Decode | Notes |
|---|---|---|
| `7bit` | Identity | Lines ≤998 octets |
| `8bit` | Identity | Requires 8BITMIME |
| `binary` | Identity | Requires BINARYMIME/CHUNKING |
| `quoted-printable` | QP | `=XX`, soft line breaks `=\r\n`; max line 76 |
| `base64` | Base64 | Ignore whitespace; validate padding |
| `x-*` | Unknown | Treat as binary; do not guess |

Be strict enough to avoid ambiguity, lenient enough to display common broken
mail. Record a defect and keep the raw bytes.

### Parameters

```
Content-Type: application/pdf; name="report.pdf"
Content-Disposition: attachment; filename="report.pdf"
```

- Quoted strings may contain escaped quotes/backslashes.
- RFC 2231 continuations: `filename*0*=utf-8''long%20name; filename*1*=.pdf`.
- Sanitize filenames: strip path separators, control characters, leading
  dots, reserved Windows names; cap length.
- Do not trust the extension or declared type; sniff dangerous types only to
  warn, never to execute.

---

## 4. Internationalization

- **SMTPUTF8** (RFC 6531) allows UTF-8 in envelope addresses and headers.
- **RFC 6532** defines UTF-8 header values; `message/global` is the message
  type.
- **RFC 2047** encoded words are still needed for non-EAI recipients:
  `=?UTF-8?B?...?=` or `=?UTF-8?Q?...?=`.
- **RFC 2231** handles non-ASCII parameters.
- **IDNA** (RFC 5890/5891) converts Unicode domains to A-labels for DNS.
- **Downgrade** (RFC 6857/6858): if a downstream cannot accept EAI, either
  downgrade (rewrite headers/addresses) or bounce. Downgrading can break
  signatures and addresses; do it only at the edge and document it.
- Do not assume UTF-8 input is valid; validate and replace invalid sequences
  with U+FFFD for display, but preserve raw bytes for signing.

---

## 5. Parsing strategy

### Preserve the raw message

Store the exact bytes received (after SMTP dot-unstuffing and CRLF framing).
DKIM verification must operate on those bytes or on a documented canonical
form. If you normalize line endings, keep the original hash and the
normalized hash.

### Stream, do not slurp

- Read headers with a cap (for example 1 MiB).
- Parse the body as a stream; decode parts lazily.
- Enforce per-part and total size limits before allocating.
- Do not recurse without a depth counter.
- Do not follow `message/external-body`, `Content-Location`, or remote URLs.

### Be liberal, but record defects

Return a `ParseResult`:

```csharp
record ParseResult(
    MimeMessage Message,
    IReadOnlyList<ParseDefect> Defects,
    string RawSha256);
```

Defects include: missing boundary, invalid base64, unknown charset, duplicate
`From`, invalid date, overlong line, truncated part, nested too deep.
Policy can quarantine, deliver with a warning header, or reject at SMTP.

### Do not re-serialize before verification

Re-serializing changes folding, encoding, and line endings. Verify DKIM on
the original bytes, then parse for display. If you must re-serialize (for
example, to add a disclaimer), re-sign the result.

### BODYSTRUCTURE / ENVELOPE cache

IMAP clients ask for `BODYSTRUCTURE` and `ENVELOPE` repeatedly. Parse once,
store a cache keyed by message ID and parser version. Invalidate on parser
upgrade; keep a migration path.

---

## 6. DKIM-sensitive canonicalization

DKIM defines two canonicalizations (RFC 6376 §3.4):

- **simple**: no changes; header names and values exactly as signed, body
  with trailing empty lines removed.
- **relaxed**: lowercase header names, collapse whitespace in values, strip
  trailing whitespace; body: collapse whitespace runs to one space, strip
  trailing whitespace at line ends, remove trailing empty lines.

Rules for implementers:

- Do not add, remove, or reorder headers between signing and verification.
- `Received`, `Return-Path`, `Delivered-To`, and `Authentication-Results`
  added after verification do not invalidate the signature.
- If you add a `Disclaimer` or rewrite `Subject`, re-sign.
- A `l=` body length tag allows signing only a prefix; treat it as a
  potential truncation attack and record it.
- Header fields listed in `h=` that do not exist are treated as empty; this
  is why oversigning matters.

Details and test vectors are in `08-email-authentication.md`.

---

## 7. Security

### Header injection

Never concatenate untrusted text into a header. Reject CR/LF in display
names, subjects, filenames, and parameters. Encode with RFC 2047 or
quoted-string rules.

### HTML

- Sanitize with an allowlist (no scripts, no event handlers, no `javascript:`
  URLs, no `data:` except safe images if policy allows).
- Block remote images/tracking pixels by default; let the user opt in.
- Rewrite links through a warning page if your threat model requires it.
- Do not execute CSS that can exfiltrate data (`url()`), or sanitize it.
- Treat SVG as active content; do not render inline.

### Attachments

- Never execute or auto-open.
- Scan with ClamAV; unpack archives with limits (depth, count, ratio, total
  size).
- Block or warn on dangerous types (`.exe`, `.scr`, `.js`, `.hta`, `.lnk`,
  macro-enabled Office, `.iso`, `.img`).
- Sanitize filenames; store under a generated ID, not the supplied name.
- Password-protected archives cannot be scanned; quarantine or warn.

### MIME confusion

- A part can claim `text/plain` but contain HTML; render according to the
  declared type or sniff and warn.
- A `multipart/signed` part can hide a different body; verify the signature
  over the exact canonical form.
- `message/rfc822` can nest a phishing message; render it as an attachment,
  not as the primary body.
- `message/partial` can be used to reassemble malware; limit or block.
- `message/external-body` can trigger network fetches; never fetch.

### Resource exhaustion

| Limit | Suggested |
|---|---|
| Header section | 1 MiB |
| Header count | 1000 |
| MIME depth | 20 |
| Part count | 1000 |
| Single part decoded | 25 MiB |
| Total decoded | 100 MiB |
| Archive depth | 3 |
| Archive entries | 1000 |
| Compression ratio | 100:1 |

Return a parse error or quarantine; never OOM the process.

### S/MIME and PGP

- Do not decrypt on the server unless you are explicitly a decryption
  gateway.
- Verify signatures only if you have a trust model; otherwise mark as
  unverified.
- Preserve `multipart/signed` and `multipart/encrypted` structures.

---

## 8. Rendering and text extraction

- Decode headers for display; keep the raw header for forensics.
- Decode the preferred `multipart/alternative` part; fall back to text.
- Convert HTML to text for indexing/preview; keep the original HTML for the
  client.
- Strip quoted replies for previews only; do not modify the stored message.
- Extract attachments for virus scanning and search; store metadata (name,
  type, size, hash, `Content-ID`).
- Generate a plain-text preview with a fixed byte limit; never include
  active content.

---

## 9. Test corpus

Keep a versioned corpus (no real PII):

- minimal `text/plain`;
- `multipart/alternative` with text + HTML;
- `multipart/mixed` with PDF and image;
- nested `multipart/related`;
- `message/rfc822` attachment;
- `multipart/report` DSN;
- `multipart/signed` S/MIME;
- encoded-word subject and display name;
- RFC 2231 filename continuations;
- 8-bit body; base64; quoted-printable with soft breaks;
- missing boundary; truncated part; invalid base64; unknown charset;
- 1000-level nested multipart (must be rejected safely);
- header with CRLF injection attempt;
- filename `../../etc/passwd`;
- HTML with `<script>`, `onerror=`, `javascript:` URL, remote image;
- EAI message (`message/global`);
- empty body; body with only whitespace; body with a single dot line;
- message with 100 `Received` headers;
- message with duplicate `From`, missing `Date`, invalid `Message-ID`.

For each, assert: parse result, defect list, rendered text, and no crash/OOM.

---

## 10. Implementation checklist

- [ ] Raw bytes are stored exactly; hash is recorded.
- [ ] Parser enforces depth/part/size limits.
- [ ] No external entity or URL fetch.
- [ ] Header injection is impossible in generated headers.
- [ ] HTML is sanitized; remote content blocked by default.
- [ ] Filenames are sanitized; attachments are stored under generated IDs.
- [ ] DKIM verification uses the original bytes.
- [ ] `BODYSTRUCTURE`/`ENVELOPE` cache is versioned.
- [ ] Malformed messages produce a defect, not a crash.
- [ ] EAI downgrade is explicit and tested.
- [ ] Fuzz corpus covers multipart, base64, QP, encoded words, and boundaries.
