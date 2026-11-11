# 12 — Operations

A mail server is judged by what happens when DNS breaks, a disk fills, a
certificate expires, or a spammer finds an account. This document is the
operational counterpart to the protocol docs.

---

## 1. DNS zone template

Replace names and IPs. Every record below has a reason.

```dns
$ORIGIN example.com.
$TTL 3600

; --- Hosts ---------------------------------------------------------------
@                   IN A      203.0.113.10
@                   IN AAAA   2001:db8::10
mx1                 IN A      203.0.113.10
mx1                 IN AAAA   2001:db8::10
mx2                 IN A      203.0.113.11
mail                IN A      203.0.113.10
mta-sts             IN A      203.0.113.10
api                 IN A      203.0.113.10
autoconfig          IN A      203.0.113.10
autodiscover        IN A      203.0.113.10

; --- Mail routing --------------------------------------------------------
@                   IN MX 10  mx1.example.com.
@                   IN MX 20  mx2.example.com.

; --- SPF -----------------------------------------------------------------
@                   IN TXT   "v=spf1 mx -all"

; --- DKIM (one selector per key; rotate) --------------------------------
selector1._domainkey IN TXT  "v=DKIM1; k=rsa; p=MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8A..."
selector2._domainkey IN TXT  "v=DKIM1; k=rsa; p=MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8A..."

; --- DMARC (RFC 9989) ----------------------------------------------------
_dmarc              IN TXT   "v=DMARC1; p=reject; sp=quarantine; np=reject; adkim=s; aspf=s; rua=mailto:dmarc@example.com; fo=0; t=n"

; --- MTA-STS -------------------------------------------------------------
_mta-sts            IN TXT   "v=STSv1; id=20261111000000Z"

; --- TLS-RPT -------------------------------------------------------------
_smtp._tls          IN TXT   "v=TLSRPTv1; rua=mailto:tlsrpt@example.com"

; --- DANE (only if DNSSEC is enabled) -----------------------------------
_25._tcp.mx1        IN TLSA  3 1 1 ( 0123456789abcdef... )
_25._tcp.mx2        IN TLSA  3 1 1 ( 0123456789abcdef... )

; --- CAA -----------------------------------------------------------------
@                   IN CAA   0 issue "letsencrypt.org"
@                   IN CAA   0 issuewild "letsencrypt.org"
@                   IN CAA   0 iodef "mailto:security@example.com"

; --- Autodiscovery (RFC 6186) -------------------------------------------
_submission._tcp    IN SRV   0 1 587 mail.example.com.
_imaps._tcp         IN SRV   0 1 993 mail.example.com.
_imap._tcp          IN SRV   0 1 143 mail.example.com.
_pop3s._tcp         IN SRV   0 1 995 mail.example.com.

; --- Policy host (served by the web host) --------------------------------
; https://mta-sts.example.com/.well-known/mta-sts.txt
```

Reverse DNS (PTR) lives in the reverse zone controlled by your IP provider:

```
10.113.0.203.in-addr.arpa. PTR mx1.example.com.
```

Every MX host must have a PTR that resolves back to the same hostname
(forward-confirmed reverse DNS). Set it before sending volume.

### DNS checklist

- [ ] A/AAAA for every MX, submission, IMAP, JMAP, MTA-STS, autoconfig host.
- [ ] MX preference and backup path tested.
- [ ] SPF record under 10 DNS lookups; `-all` once stable.
- [ ] DKIM selector published before signing; old selector kept for rotation.
- [ ] DMARC record valid under RFC 9989; monitor `rua`.
- [ ] MTA-STS TXT `id` matches the served policy.
- [ ] TLS-RPT record points to a monitored mailbox.
- [ ] TLSA records match current certs; DNSSEC enabled.
- [ ] CAA restricts issuance.
- [ ] PTR for every sending IP.
- [ ] No wildcard MX; no null MX unless intentional.

---

## 2. Deployment topology

### Small / single node

```
Internet
  │
  ├─ :25/:587/:465/:143/:993/:443
  ▼
[ Delily.Host ]──[ PostgreSQL ]──[ blob volume ]
  │
  ├─ rspamd / ClamAV (local sockets)
  └─ reverse proxy (JMAP/admin/autoconfig)
```

- One host, persistent volumes for DB and blobs.
- Backups to a separate host/object store.
- Monitoring agent on the host.

### HA

```
              ┌─ SMTP ingress (MX1) ─┐
Internet ─────┤                      ├─▶ queue/DB (primary + replica)
              └─ SMTP ingress (MX2) ─┘
                       │
                delivery workers (N)
                       │
                 blob store (replicated/S3)
```

- Two MX hosts with independent queues or a shared durable queue.
- PostgreSQL primary + synchronous replica; automated failover.
- Blob store replicated or object storage with versioning.
- Load balancer for JMAP/IMAP (IMAP sticky sessions if session state is
  in-process; better to externalize state).
- Keep delivery egress IPs stable; do not NAT through random addresses.

---

## 3. Ports and firewall

| Port | Protocol | Exposure |
|---|---|---|
| 25 | SMTP | Public |
| 587 | Submission STARTTLS | Public |
| 465 | Submissions (implicit TLS) | Public |
| 143 | IMAP STARTTLS | Public (or VPN) |
| 993 | IMAPS | Public |
| 110/995 | POP3/POP3S | Only if needed |
| 4190 | ManageSieve | Public with TLS, or VPN |
| 443 | HTTPS: JMAP, HTTP submission API, autoconfig | Public |
| 8080 | Admin/API | Private only |
| 5432 | PostgreSQL | Private only |
| 6379 | Redis | Private only |

Default-deny inbound; allow only the above. Outbound: 25 to the Internet,
443 for ACME/HTTP fetches, DNS to your resolver. Block outbound 25 from
application containers except the delivery worker.

---

## 4. Configuration and secrets

- Environment variables or a secret manager; never commit secrets.
- Separate config per environment; validate at startup and fail fast.
- Secrets: DB password, DKIM private keys, TLS keys, OAuth client secrets,
  API keys, webhook signing secrets, SMTP relay credentials.
- API keys: store only hashes, show once, bind to a tenant + scopes, support
  rotation with an overlap window, and audit creation/revocation.
- Webhooks: egress allowlist/deny private IPs, sign payloads with HMAC, cap
  retries, and alert on sustained failures.
- Rotate: DKIM keys (6–12 months), TLS certs (auto), DB passwords (90 days),
  API tokens (90 days).
- Use a KMS/HSM for DKIM keys if available; otherwise files with `0600` and
  full-disk encryption.
- Audit secret access.

---

## 5. Observability

### Metrics

| Area | Metrics |
|---|---|
| SMTP | connections, messages accepted/rejected by reason, auth failures, TLS handshakes, command latency |
| Queue | depth by status/domain, oldest age, attempts, DSN count |
| Delivery | success rate, 4xx/5xx by domain, connect/TLS/DATA latency |
| IMAP | sessions, commands/sec, FETCH bytes, search latency, IDLE count |
| Storage | DB size, WAL lag, blob bytes, orphan count, disk free |
| Auth | SPF/DKIM/DMARC/ARC pass rates, report volume |
| Abuse | spam score distribution, quarantine rate, blocklist hits |
| HTTP | request rate, 4xx/5xx, p95 latency, auth failures, submission 202/4xx, rate-limit hits |
| Webhooks | delivery success/failure, retry depth, signature failures |

### Logs

Structured JSON with:

- timestamp, level, service, host;
- `session_id`, `queue_id`, `message_id` (hashed if PII), `tenant_id`;
- peer IP, HELO, envelope from/to (redacted as policy requires);
- auth results, policy decision, response code;
- duration.

Do not log passwords, tokens, full message bodies, or attachment contents.
Set retention and access controls.

### Traces

Trace a message across SMTP accept → auth → queue → delivery → DSN. Use
OpenTelemetry with baggage for `queue_id`.

### Alerts

| Condition | Severity |
|---|---|
| Oldest queued message > 1 h | Page |
| Queue depth > threshold for 15 min | Page |
| Disk < 15% free | Page |
| Cert expires < 14 days | Ticket; < 7 days page |
| DB replication lag > 60 s | Page |
| SMTP 5xx rate > 10% for 10 min | Page |
| Auth failure spike > 10× baseline | Page |
| Blocklist listing | Ticket |
| DMARC/TLS-RPT report flood | Ticket |
| HTTP API 401/429 spike | Page/ticket |
| Webhook failure backlog | Ticket |
| rspamd/ClamAV stale | Ticket |
| Backup failed | Page |

---

## 6. Runbooks

### Queue backup

1. Check `queue` depth and oldest age.
2. Inspect top errors by domain.
3. Check DNS, TLS, and remote 4xx/5xx.
4. Pause new submissions if the backlog threatens disk.
5. Fix the cause; resume; monitor drain rate.
6. If a domain is broken, use a per-domain circuit breaker; do not retry
   every message.

### Disk full

1. Alert; stop accepting new mail (`451 4.3.1`).
2. Free space: rotate logs, purge temp files, expire quarantine/trash per
   policy.
3. Never delete queue rows or blobs manually without an audit.
4. Verify DB and blob store integrity after recovery.
5. Add capacity and alert thresholds.

### Certificate expiry

1. Check `openssl s_client` for each hostname.
2. Renew via ACME; reload the listener without dropping connections if
   possible.
3. Verify chain and hostname; run `testssl.sh`.
4. Update MTA-STS/DANE if the key changed (DANE especially).

### DNS failure

1. Check resolver health and DNSSEC.
2. If outbound DNS fails, queue mail with 4xx and retry; do not bounce.
3. If inbound MX DNS fails, you are unreachable; escalate to the DNS provider.
4. Verify SPF/DKIM/DMARC/MTA-STS still resolve after recovery.

### Blocklist listing

1. Identify the list and the reason (spam trap, complaint, compromised
   account, misconfiguration).
2. Stop the source; rotate credentials if compromised.
3. Remediate; request delisting with evidence.
4. Monitor relisting; adjust rate limits and authentication.

### Abuse spike / compromised account

1. Identify the account/IP and volume.
2. Suspend the account or throttle the IP.
3. Preserve logs; notify the customer per policy.
4. Clean up (malware, forwarding rules, OAuth tokens).
5. Report to upstream if required.

### Mail loop

1. Inspect `Received` counts and `Message-ID`.
2. Identify the looping pair (forwarding, alias, list).
3. Disable the rule; add `X-Loop`/`Auto-Submitted` checks.
4. Purge the looped queue entries carefully.

### Database failover

1. Promote the replica; update the connection string.
2. Verify `UIDNEXT`/`UIDVALIDITY` consistency.
3. Restart workers; they should reclaim `inflight` rows.
4. Run integrity checks and a sample IMAP fetch.

### DKIM key compromise

1. Retire the selector immediately; publish `p=` empty.
2. Generate a new key/selector; sign with it.
3. Keep the old public key for verification if messages are in flight.
4. Notify the domain owner; check DMARC reports for abuse.

### API key compromise

1. Revoke the key; notify the tenant; preserve logs.
2. Check for unauthorized sends, recipient patterns, and data access.
3. Rotate the signing secret and any webhook secrets.
4. Tighten rate limits/scopes; add a regression test.

### Webhook failures

1. Inspect HTTP status, timeout, and DNS/TLS failures.
2. Verify the tenant endpoint is HTTPS and not a private IP.
3. Replay from the event log after the endpoint recovers; avoid duplicate
   side effects by including the event ID.

### Data breach

1. Contain; preserve evidence; rotate secrets.
2. Assess scope (mailboxes, credentials, logs).
3. Notify per legal requirements; engage counsel.
4. Fix the root cause; update the threat model and tests.

---

## 7. Backup and disaster recovery

- PostgreSQL PITR with WAL archiving.
- Blob store snapshots/versioning.
- Config and secrets backup (encrypted, separate).
- Restore drills monthly; measure RTO/RPO.
- Keep an offline copy for ransomware resilience.
- Test restoring a single mailbox and a full node.

---

## 8. Capacity planning

| Resource | Rule of thumb |
|---|---|
| Storage per user | 1–10 GB (plan-dependent); attachments dominate |
| IOPS | Blob writes are sequential; IMAP random reads; DB WAL sync |
| SMTP connections | 10–50 per 1,000 users, bursty |
| IMAP connections | 1–3 per active user; IDLE holds one |
| Queue workers | 2–4 per 1,000 messages/min |
| DB | Strongly consider managed PostgreSQL for HA/backups |
| Bandwidth | Inbound + outbound message size × volume; TLS overhead |

Measure before scaling. A single node can handle a small domain; do not
prematurely distribute.

---

## 9. Compliance and privacy

- **GDPR/CCPA**: lawful basis, data minimization, retention, access/erasure,
  processor agreements, data residency.
- **Logs**: avoid message content; redact PII; short retention.
- **DMARC/TLS-RPT reports**: contain IPs and domains; treat as personal data
  where applicable.
- **Encryption**: TLS in transit, disk encryption at rest, encrypted backups.
- **Access control**: least privilege, MFA for admins, audit trail.
- **Lawful access**: documented process, legal review, transparency report.
- **Abuse**: published policy, appeals, cooperation with authorities.
- **DPIA**: content scanning, spam filtering, and retention may require one.

This is not legal advice; have counsel review before launch.

---

## 10. Launch checklist

See [`checklists/launch-readiness.md`](checklists/launch-readiness.md). At a
minimum:

- [ ] DNS complete and verified from multiple resolvers.
- [ ] TLS A grade; MTA-STS enforce; TLS-RPT receiving.
- [ ] SPF/DKIM/DMARC pass for outbound; DMARC reports arriving.
- [ ] Open relay test fails; auth bypass test fails.
- [ ] SMTP smuggling tests pass.
- [ ] Queue survives restart; DSNs correct.
- [ ] IMAP/JMAP interop with at least three clients.
- [ ] HTTP submission API auth, authorization, idempotency, and limits tested.
- [ ] Backups restore; scrub passes.
- [ ] Monitoring/alerts live; runbooks reviewed.
- [ ] Abuse/postmaster contacts monitored.
- [ ] Rate limits and quotas enforced.
- [ ] Privacy/retention policy published.
