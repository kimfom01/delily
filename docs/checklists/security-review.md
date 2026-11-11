# Security review checklist

Run before exposing a new listener, before a major release, and after any
architecture change. Record findings with severity and owner.

---

## 1. Threat model

- [ ] Assets: messages, credentials, keys, metadata, reputation.
- [ ] Actors: anonymous sender, authenticated user, malicious peer, insider,
  compromised account, network attacker.
- [ ] Entry points: 25, 587, 465, 143, 993, 4190, 443, admin API, DNS, ACME.
- [ ] Trust boundaries: Internet → edge → queue → storage → admin.
- [ ] Abuse cases: open relay, spoofing, spam, phishing, DoS, data exfiltration.
- [ ] Assumptions documented (e.g., DNSSEC available, OS patched).

## 2. Authentication and authorization

- [ ] Passwords hashed with Argon2id; no plaintext/reversible storage.
- [ ] TLS required before credentials.
- [ ] SASL mechanisms implemented correctly; no downgrade.
- [ ] OAuth token validation (issuer, audience, expiry, signature).
- [ ] Session fixation/rotation handled.
- [ ] Brute-force rate limits and lockout.
- [ ] Sender authorization: user cannot spoof another domain.
- [ ] Tenant isolation on every query; cross-tenant IDs return 404.
- [ ] Admin actions require MFA and audit.

## 3. SMTP

- [ ] No open relay; relay only for local/authorized peers.
- [ ] `RCPT` validation does not leak directory info beyond policy.
- [ ] Command/line/size/recipient limits.
- [ ] Bare LF/CR rejected or normalized; smuggling payloads fail.
- [ ] `PIPELINING`/`BDAT` framing safe.
- [ ] `STARTTLS` resets state; no plaintext auth.
- [ ] `VRFY`/`EXPN` do not leak addresses.
- [ ] `Received`/trace headers cannot be injected.
- [ ] Null sender handling prevents backscatter.

## 4. Message parsing

- [ ] Depth/part/size/archive limits.
- [ ] No external entity/URL fetch.
- [ ] Header injection impossible in generated headers.
- [ ] HTML sanitized; scripts/event handlers/`javascript:` blocked.
- [ ] Remote content blocked by default.
- [ ] Attachment filenames sanitized; stored under generated IDs.
- [ ] `message/external-body`, `message/partial`, `multipart/encrypted`
  handled safely.
- [ ] Fuzz corpus runs; no crashes/hangs/OOM.

## 5. IMAP/JMAP

- [ ] Literal/command size limits; no desync.
- [ ] Sequence/UID handling cannot access another mailbox.
- [ ] ACL checked on every command.
- [ ] Search respects ACL/tenant.
- [ ] JMAP auth, CORS, CSRF, rate limits.
- [ ] Blob download cannot traverse paths or read other accounts.
- [ ] Sieve sandboxed/time-limited; no file/network access.

## 5b. HTTP submission API

- [ ] API keys hashed at rest; scoped, expiring, revocable.
- [ ] OAuth/JWT validation (issuer, audience, expiry, signature).
- [ ] From-domain/tenant authorization; no open relay.
- [ ] Header allowlist; CRLF injection rejected.
- [ ] Attachment size/type limits; no URL fetch.
- [ ] Idempotency key semantics and storage.
- [ ] `202` only after durable commit.
- [ ] Rate limits and abuse suspension.
- [ ] Cross-tenant IDs return `404`.
- [ ] Webhook SSRF protections and HMAC.
- [ ] OpenAPI contract test in CI.
- [ ] No bodies/credentials in logs.

## 6. Queue and storage

- [ ] Accept only after durable commit.
- [ ] Queue rows cannot be injected/deleted by users.
- [ ] Blob path is hash-derived; no client filename.
- [ ] GC cannot delete referenced blobs.
- [ ] Backup/restore encrypted; access controlled.
- [ ] Deletion honors legal hold/GDPR.

## 7. TLS and DNS

- [ ] TLS 1.0/1.1 disabled; strong ciphers.
- [ ] Cert validation on outbound; no `--insecure` equivalent.
- [ ] MTA-STS/DANE enforcement cannot be downgraded.
- [ ] `REQUIRETLS` honored.
- [ ] SNI/hostname verification correct.
- [ ] HSTS/CSP/security headers on HTTP.
- [ ] DNS rebinding/SSRF blocked for policy fetches.
- [ ] DNSSEC failures not silently trusted.

## 8. Secrets and dependencies

- [ ] No secrets in git, logs, crash dumps, or test fixtures.
- [ ] Secret manager/KMS; least privilege.
- [ ] DKIM/TLS key rotation documented.
- [ ] Dependency audit: no critical/high; pinned versions.
- [ ] Container images minimal, non-root, read-only where possible.
- [ ] SBOM generated.

## 9. Logging and privacy

- [ ] Passwords/tokens/full bodies never logged.
- [ ] PII redacted; retention policy enforced.
- [ ] Audit log tamper-evident.
- [ ] Error messages do not leak internals.
- [ ] DMARC/TLS-RPT reports handled under privacy policy.

## 10. Resilience

- [ ] Rate limits/connection caps.
- [ ] Disk-full/DB-down behavior tested.
- [ ] Queue poisoning bounded.
- [ ] Mail loop detection.
- [ ] Backup restore tested.
- [ ] Incident response contacts/runbooks.

## 11. Findings

| ID | Severity | Finding | Owner | Status |
|---|---|---|---|---|
| | Critical/High/Medium/Low | | | Open/Fixed/Accepted |

Critical/high findings block launch. Accepted risks need a written rationale
and an expiry date.
