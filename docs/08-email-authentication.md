# 08 — Email authentication: SPF, DKIM, DMARC, ARC

**Normative base:** RFC 7208 (SPF), RFC 6376 + 8301 + 8463 (DKIM), RFC 9989
(DMARC, May 2026), RFC 9990/9991 (DMARC reporting), RFC 8617 (ARC),
RFC 8601 (`Authentication-Results`), RFC 6531/6532/8616 (EAI interaction).

**Read this first:** DMARC changed in 2026. RFC 9989 obsoletes RFC 7489 and
RFC 9091. The current record grammar has `p`, `sp`, `np`, `adkim`, `aspf`,
`fo`, `rua`, `ruf`, `psd`, and `t`. It does **not** define `pct`, `rf`, or
`ri`. Do not copy those tags from older guides.

---

## 1. Evaluation order

At the receiving edge:

```
1. Connection / HELO identity
2. SPF        (MAIL FROM, or HELO when MAIL FROM is null)
3. DKIM       (every DKIM-Signature present)
4. DMARC      (RFC5322.From + aligned SPF/DKIM)
5. ARC        (chain validation, if present)
6. Local policy (spam, virus, reputation, user rules)
```

Record each verdict in `Authentication-Results` with your own hostname.
Never trust an inbound `Authentication-Results` header from an untrusted
source; keep it for forensics but add your own at the top.

```
Authentication-Results: mx.example.com;
  spf=pass smtp.mailfrom=example.com;
  dkim=pass header.d=example.com header.s=sel1 header.b=abc123;
  dmarc=pass header.from=example.com;
  arc=pass
```

`Received-SPF` is optional but useful:

```
Received-SPF: Pass (domain of example.com designates 192.0.2.10 as permitted sender)
  client-ip=192.0.2.10; envelope-from=user@example.com; helo=mail.example.com;
```

---

## 2. SPF (RFC 7208)

### Record lookup

- Query TXT for the domain (MAIL FROM domain; HELO domain if the reverse-path
  is null).
- If no TXT record: result `none`.
- If more than one record starts with `v=spf1`: `permerror`.
- If the record is syntactically invalid: `permerror`.
- If DNS is temporarily unavailable: `temperror`.
- A record may be split across multiple character-strings; concatenate them
  without separators.

Example:

```
example.com.  TXT  "v=spf1 mx a:mail.example.com ip4:192.0.2.0/24 -all"
```

### Mechanisms

| Mechanism | Meaning |
|---|---|
| `all` | Matches everything |
| `include:<domain>` | Evaluate another domain's SPF; a `pass` here is a pass |
| `a[:domain][/cidr]` | Match the domain's A/AAAA |
| `mx[:domain][/cidr]` | Match the domain's MX hosts |
| `ptr[:domain]` | Reverse DNS (deprecated; avoid) |
| `ip4:<ip>[/cidr]` | Match IPv4 |
| `ip6:<ip>[/cidr]` | Match IPv6 |
| `exists:<domain>` | Match if the domain resolves |
| `redirect=<domain>` | Replace the record if no mechanism matched |
| `exp=<domain>` | Explanation TXT for `fail` |

Qualifiers: `+` (pass, default), `-` (fail), `~` (softfail), `?` (neutral).

### Evaluation rules

- Evaluate mechanisms left to right; the first match wins.
- `include` evaluates the included domain's record. A recursive `pass`
  matches. `fail`, `softfail`, and `neutral` do **not** match; evaluation
  continues with the parent record. A recursive `temperror` or `permerror`
  is returned immediately, and a recursive `none` becomes `permerror`.
- `redirect` is only used if no mechanism matched; it replaces the current
  record and counts against the lookup limit.
- `exp` is only consulted for a `fail`, and only for the explanation text.
- The default result if no mechanism matches is `neutral`.
- **DNS lookup limit:** at most 10 mechanisms/modifiers that cause DNS
  queries (`include`, `a`, `mx`, `ptr`, `exists`, `redirect`). Exceeding it is
  `permerror`.
- **Void lookup limit:** at most 2 DNS queries that return no answer.
  Exceeding it is `permerror`.
- A DNS timeout is `temperror`; do not cache it as a permanent result.

### Macros

Macros appear in mechanism domains, `exp`, and `redirect`:

| Macro | Expands to |
|---|---|
| `%{s}` | Sender (MAIL FROM local@domain) |
| `%{l}` | Local-part |
| `%{o}` | Domain |
| `%{d}` | Current domain |
| `%{i}` | Client IP (dotted or IPv6) |
| `%{p}` | Validated domain (PTR) |
| `%{v}` | `in-addr` or `ip6` |
| `%{h}` | HELO/EHLO domain |

Transformers: `r` (reverse), `l` (lowercase), `u` (uppercase), `d` (domain
part), digits (keep rightmost labels). Escapes: `%%`, `%_` (space), `%-`
(`%20`). Implement the grammar exactly; macro expansion bugs are a common
source of bypasses.

### Results and headers

| Result | Meaning |
|---|---|
| `none` | No SPF record |
| `neutral` | Record exists but no mechanism matched |
| `pass` | Authorized |
| `fail` | Not authorized (use `-all`) |
| `softfail` | Probably not authorized (use `~all`) |
| `temperror` | DNS/temporary error |
| `permerror` | Invalid record / lookup limit |

### Edge cases

- **Forwarding** breaks SPF because the new sender IP is not authorized.
  ARC or SRS can help; SRS rewrites the envelope and is not standardized.
- **Multiple TXT records** are common (verification tokens). Only the
  `v=spf1` record counts; more than one is `permerror`.
- **CNAME**: resolve to the canonical name; avoid loops.
- **`include` loops**: cap the total lookup count and detect recursion.
- **`ptr`**: slow, unreliable, and deprecated by many operators; do not use it
  for new domains.
- **HELO identity**: when `MAIL FROM:<>`, evaluate the HELO domain. DMARC's
  SPF alignment may still be undefined; do not pretend it passed.
- **IPv4-mapped IPv6**: normalize before matching.
- **`+all`**: a valid record that authorizes everyone; treat as a policy
  failure signal, not a pass you want.

---

## 3. DKIM (RFC 6376, 8301, 8463)

### Signature header

```
DKIM-Signature: v=1; a=rsa-sha256; c=relaxed/relaxed;
  d=example.com; s=selector1; t=1762862400;
  h=from:to:subject:date:message-id:mime-version:content-type;
  bh=base64bodyhash;
  b=base64signature
```

### Tags

| Tag | Meaning |
|---|---|
| `v=1` | Version (required, first) |
| `a=` | Algorithm: `rsa-sha256`, `ed25519-sha256` |
| `c=` | Canonicalization: `header/body`, each `simple` or `relaxed` |
| `d=` | Signing domain |
| `s=` | Selector |
| `h=` | Signed header fields, colon-separated, in order |
| `bh=` | Body hash (base64) |
| `b=` | Signature (base64) |
| `i=` | Agent/user identifier (AUID) |
| `l=` | Body length limit (dangerous) |
| `q=` | Query method (`dns/txt`) |
| `t=` | Signature timestamp |
| `x=` | Expiration timestamp |
| `z=` | Copied header fields (debug only) |
| `n=` | Notes |

Unknown tags are ignored. `v`, `a`, `d`, `s`, `h`, `bh`, `b` are required for
verification.

### Signing

1. Choose headers: at minimum `From`, `Subject`, `Date`, `Message-ID`,
   `MIME-Version`, `Content-Type`; include `To`, `Cc`, `Reply-To`,
   `In-Reply-To`, `References` when present.
2. Canonicalize the body (simple or relaxed) and compute `bh`.
3. Build the `DKIM-Signature` header with empty `b=`.
4. Canonicalize the selected headers (including the signature header with an
   empty `b=`), in the order listed in `h=`.
5. Hash and sign with the private key.
6. Insert the base64 signature into `b=`.
7. Do not modify the message after signing.

**Oversigning:** include a header name in `h=` more times than it appears, so
an attacker cannot add an unsigned copy. At minimum, oversign `From`.

### Verification

1. Parse every `DKIM-Signature`; ignore malformed ones with a recorded reason.
2. Check `v=1`, `a=` supported, `d=`/`s=` present, `h=` includes `From`.
3. Look up `s._domainkey.d` TXT.
4. Parse the key record: `v=DKIM1`, `k=rsa|ed25519`, `p=base64`,
   optional `h=`, `s=`, `t=`, `n=`.
5. `p=` empty means revoked: fail.
6. RSA key < 1024 bits: fail (RFC 8301); prefer 2048.
7. Canonicalize the body and compare `bh`; if `l=` is present, only the first
   `l` octets are signed.
8. Canonicalize the signed headers and verify `b`.
9. Check `x=` expiration and `i=` AUID (must be `d=` or a subdomain).
10. Return `pass`, `fail`, `neutral`, `temperror`, or `permerror`.

### Canonicalization

**simple header:** exact bytes, including folding, case, and whitespace.

**relaxed header:**
- lowercase the field name,
- unfold,
- collapse whitespace runs to one space,
- strip trailing whitespace,
- remove whitespace around the colon,
- end with CRLF.

**simple body:** no changes; remove trailing empty lines.

**relaxed body:**
- collapse whitespace runs to one space,
- strip trailing whitespace at line ends,
- remove trailing empty lines,
- end with CRLF (unless the body is empty).

The empty body canonicalizes to a single CRLF; get this right or every
empty-body signature will fail.

### Key records

```
selector1._domainkey.example.com. TXT
  "v=DKIM1; k=rsa; p=MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8A..."
```

- Multiple character-strings are concatenated.
- `k=rsa` is the default; `k=ed25519` for Ed25519.
- `h=sha256` restricts hash algorithms.
- `s=email` restricts service type (optional).
- `t=y` testing mode; `t=s` strict (no subdomain AUID).
- `n=` notes.
- Publish the public key at least a few TTLs before signing with it.

### Edge cases

- **Header duplication:** if `h=` lists `from` twice and there are two `From`
  headers, both are signed; if only one exists, the second is treated as
  empty. This is why oversigning works.
- **`l=` truncation:** a signer can sign only the first N bytes; an attacker
  can append content. Treat `l=` as suspicious; still verify, but do not
  trust the unsigned tail for policy.
- **`z=`:** copied headers are not covered by the signature; never use them
  for display or policy.
- **`i=`:** must be `d=` or a subdomain; otherwise fail.
- **`x=`:** expired signatures fail; some verifiers ignore `x`, but you
  should not.
- **Replay:** DKIM does not prevent replay; DMARC/ARC/reputation do.
- **Mailing lists:** subject/body modifications break DKIM; ARC preserves the
  original assessment.
- **DNS:** multiple TXT strings, CNAMEs, wildcard keys, and temporary
  failures; cache with TTL but not beyond.
- **Key rotation:** publish new selector, sign with it, keep the old selector
  for verification until the maximum signature lifetime has passed.
- **Revocation:** publish `p=` empty; verifiers must fail.
- **8-bit/UTF-8:** canonicalization operates on octets; do not decode before
  hashing.
- **Line endings:** canonicalize to CRLF; bare LF changes the hash.

### Signing policy for outbound

- Sign every outbound message with at least one domain-aligned selector.
- Use 2048-bit RSA or Ed25519; keep the private key in a KMS/HSM if possible.
- Sign after all content modifications and before queueing.
- If you must add a footer/disclaimer, do it before signing.
- Do not sign with a selector you cannot revoke.
- Publish `_domainkey` and DMARC records before sending volume.

---

## 4. DMARC (RFC 9989)

### Record discovery

1. Query `_dmarc.<domain>` TXT.
2. If no record, walk up to the organizational domain (PSL) and query
   `_dmarc.<org-domain>`.
3. A record at a subdomain is used for that subdomain; `sp` applies to
   existing subdomains, `np` to non-existent subdomains.
4. `psd=y` marks a public suffix domain; follow the PSD rules.
5. If multiple records are returned, discard all (no policy).
6. `v=DMARC1` must be the first tag; otherwise ignore the record.

### Tags (RFC 9989)

| Tag | Values | Meaning |
|---|---|---|
| `v` | `DMARC1` | Required, first |
| `p` | `none`, `quarantine`, `reject` | Policy for the domain and subdomains |
| `sp` | same | Policy for existing subdomains |
| `np` | same | Policy for non-existent subdomains |
| `adkim` | `r`, `s` | DKIM alignment: relaxed (default) or strict |
| `aspf` | `r`, `s` | SPF alignment: relaxed (default) or strict |
| `fo` | `0`, `1`, `d`, `s` | Failure-report options |
| `rua` | URI list | Aggregate reports |
| `ruf` | URI list | Failure reports |
| `psd` | `y`, `n`, `u` | Public suffix domain flag |
| `t` | `y`, `n` | Test mode (policy one level lower) |

**Removed from RFC 7489:** `pct`, `rf`, `ri`. Ignore them if present; do not
implement percentage rollout via `pct`.

### Alignment

- **SPF alignment:** the SPF-authenticated domain (the MAIL FROM identity;
  for a null path RFC 7208 §2.4 defines it as `postmaster@<HELO>`, so the
  HELO domain is used) must match the RFC5322.From domain.
  - relaxed: organizational domains match;
  - strict: exact match.
- **DKIM alignment:** the `d=` domain of a passing signature must match the
  From domain.
  - relaxed: organizational domains match;
  - strict: exact match.
- DMARC passes if **at least one** aligned SPF pass or aligned DKIM pass.

Example: `From: user@mail.example.com`, SPF passes for `example.com`:
relaxed alignment passes; strict fails.

### Policy application

| Result | `p=none` | `p=quarantine` | `p=reject` |
|---|---|---|---|
| Pass | Deliver | Deliver | Deliver |
| Fail | Deliver (monitor) | Junk/quarantine | Reject or discard |
| Temp error | Deliver | Deliver/defer | Defer/deliver per local policy |

- `t=y` applies one level below the declared policy (reject → quarantine,
  quarantine → none) and is a request, not a guarantee.
- Local policy may override, but log the override.
- Reject at SMTP when possible (`550 5.7.1`) to avoid backscatter; if you
  accepted the message, quarantine/discard rather than bounce.
- Do not reject based on DMARC for mailing lists that legitimately break
  alignment without ARC; use your own judgement and reputation.

### Reporting

**Aggregate reports (RFC 9990)** are XML, usually gzipped, sent daily to
`rua`:

```xml
<feedback xmlns="urn:ietf:params:xml:ns:dmarc-2.0">
  <version>1.0</version>
  <report_metadata>
    <org_name>Example Receiver</org_name>
    <email>dmarc@example.net</email>
    <report_id>...</report_id>
    <date_range><begin>...</begin><end>...</end></date_range>
  </report_metadata>
  <policy_published>
    <domain>example.com</domain>
    <adkim>r</adkim><aspf>r</aspf>
    <p>reject</p><sp>quarantine</sp><np>reject</np>
  </policy_published>
  <record>
    <row>
      <source_ip>192.0.2.10</source_ip>
      <count>5</count>
      <policy_evaluated>
        <disposition>none</disposition>
        <dkim>pass</dkim><spf>pass</spf>
      </policy_evaluated>
    </row>
    <identifiers>
      <header_from>example.com</header_from>
    </identifiers>
    <auth_results>
      <dkim><domain>example.com</domain><result>pass</result></dkim>
      <spf><domain>example.com</domain><result>pass</result></spf>
    </auth_results>
  </record>
</feedback>
```

The sample is illustrative; validate generated XML against the RFC 9990
schema before shipping.

- Send only to authorized external destinations: if the `rua`/`ruf` domain
  differs from the policy domain, check
  `_report._dmarc.<destination-domain>` for `v=DMARC1`.
- Honor `fo` for failure reports; default `0` means report only when all
  mechanisms fail.
- `ruf` reports contain message details; redact PII and rate-limit them.
- Do not send reports for domains you do not evaluate.
- Aggregate by source IP, aligned result, and disposition; cap report size.

**Failure reports (RFC 9991)** are ARF-like `message/feedback-report`
messages. They can leak content; many receivers disable them by default or
redact headers. If you generate them, include only the headers needed to
debug and respect privacy law.

### DMARC edge cases

- **No record:** `none`; do not apply a policy.
- **Record at org domain only:** applies to subdomains via `sp`/`p`.
- **Non-existent subdomain:** `np` applies; this is new in RFC 9989 and is a
  common spoofing vector.
- **PSD:** public suffix operators use `psd=y`; ordinary domains should not.
- **Multiple `From`:** DMARC requires a single domain; reject or treat as
  fail.
- **Display-name spoofing:** DMARC only protects the domain, not the display
  name.
- **Lookalike domains:** out of scope; use reputation/BIMI.
- **Forwarding:** SPF breaks; DKIM may survive; ARC can carry the assessment.
- **Mailing lists:** may rewrite `Subject`/`From`; ARC is the mitigation.
- **`p=reject` + null reverse-path:** do not bounce; reject at SMTP or
  discard.
- **Reporting address external:** verify `_report._dmarc`.
- **Report volume:** sample if a domain sends millions of messages; do not
  blow up your queue.
- **Privacy:** aggregate reports are safer than failure reports; default
  `ruf` off unless the domain asks.

---

## 5. ARC (RFC 8617)

ARC is **Experimental** but widely deployed by large forwarders. It records
an authentication assessment at each hop so a downstream receiver can trust
the chain even when SPF breaks.

### Headers

| Header | Purpose |
|---|---|
| `ARC-Authentication-Results` (AAR) | Auth verdicts at this hop |
| `ARC-Message-Signature` (AMS) | DKIM-like signature over the message |
| `ARC-Seal` (AS) | Signature over the ARC chain |

Each has `i=<instance>` (1..50). The set is ordered by `i`.

### Sealing (when you are an intermediary)

1. Compute your auth results.
2. Add `ARC-Authentication-Results: i=N; ...`.
3. Add `ARC-Message-Signature: i=N; ...` (like DKIM, covering the message).
4. Add `ARC-Seal: i=N; cv=...; ...` covering all ARC headers.
5. Set `cv=none` if there was no prior chain, `cv=pass` if the prior chain
   validated, `cv=fail` if it did not.
6. Sign with your domain/selector.

### Validation

1. Collect AS/AMS/AAR by `i`.
2. Verify the AS chain from the lowest instance upward; each AS covers the
   prior chain.
3. Verify each AMS against the message.
4. If any step fails, the chain is `fail`; do not use the AAR for policy.
5. If the chain passes, use the AAR to recover the original SPF/DKIM/DMARC
   assessment, subject to your trust policy for the forwarder.

### Cues

- ARC is not a replacement for DMARC. It is a signal for forwarding.
- Trust only forwarders you recognize; an attacker can seal their own chain.
- Do not seal at final delivery unless you are forwarding.
- Preserve the chain when forwarding; do not reorder headers.
- Limit chain length (50) and header size.

---

## 6. `Authentication-Results` and local policy

Add one header at the top:

```
Authentication-Results: mx.example.com;
  spf=pass smtp.mailfrom=example.com;
  dkim=pass header.d=example.com header.s=sel1;
  dmarc=pass header.from=example.com;
  arc=pass
```

- Use the `authserv-id` of your host.
- Include enough detail to debug (`header.d`, `header.s`, `smtp.mailfrom`,
  `smtp.helo`, `policy`).
- Do not include untrusted text that could break the header.
- Strip or namespace inbound `Authentication-Results` from other hosts; keep
  them below yours for forensics.
- Local policy can combine auth results with reputation, SPF/DKIM/DMARC, and
  user rules; record the final decision in the message trace and audit log.

---

## 7. Test plan

### SPF

- `spfquery -ip=192.0.2.10 -sender=user@example.com -helo=mail.example.com`
- Test `include`, `redirect`, `exists`, `ip4`, `ip6`, `a`, `mx`, macros.
- Test >10 lookups, >2 void lookups, multiple records, syntax errors, DNS
  timeout.
- Test null reverse-path + HELO.

### DKIM

- `dkimpy -s selector -d example.com -k key.pem` sign/verify.
- `opendkim-testmsg`.
- Test simple/relaxed, empty body, `l=`, `x=`, `i=`, duplicate headers,
  revoked key, key <1024, Ed25519, DNS multiple strings, body tampering.
- Verify that adding `Received` after signing still passes.

### DMARC

- `checkdmarc example.com`.
- Test `p=none/quarantine/reject`, `sp`, `np`, `adkim/aspf` strict/relaxed,
  `t=y`, `psd`, external `rua`, no record, multiple records.
- Test a forwarded message with broken SPF but valid DKIM.
- Test a message with a valid DKIM signature for a different domain (no
  alignment).
- Validate aggregate XML against RFC 9990; check `report_id`, date range,
  policy published, records.

### ARC

- Use OpenARC or a test harness to seal/verify a chain.
- Test `cv=none`, `cv=pass`, `cv=fail`, tampered AMS, missing instance,
  duplicate instance, chain >50.
- Test that a valid ARC chain lets a forwarder's DMARC result survive.

### End-to-end

- Send to Gmail/Yahoo/Outlook and inspect `Authentication-Results`.
- Use `mail-tester`, `port25 verifier`, or `dmarcian` for an external view.
- Check DMARC aggregate reports arrive and parse.
- Check failure reports are only sent when requested and authorized.
