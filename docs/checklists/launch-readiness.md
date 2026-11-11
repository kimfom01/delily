# Launch readiness checklist

Do not open port 25 to the public Internet until every item is true or has a
signed, time-bound exception.

---

## 1. DNS

- [ ] A/AAAA for every MX, submission, IMAP, JMAP, MTA-STS, autoconfig host.
- [ ] MX records with a working backup; preference tested.
- [ ] PTR for every sending IP; forward-confirmed.
- [ ] SPF record valid, under 10 lookups, `-all` (or documented `~all`).
- [ ] DKIM selector published; test signature verifies externally.
- [ ] DMARC record valid under RFC 9989; `rua` monitored.
- [ ] MTA-STS TXT + HTTPS policy; `id` matches.
- [ ] TLS-RPT record; reports arriving.
- [ ] DANE TLSA (if DNSSEC); `danetest` passes.
- [ ] CAA restricts issuance.
- [ ] SRV/autoconfig records work.
- [ ] DNSSEC validated; no bogus signatures.

## 2. TLS and certificates

- [ ] No TLS 1.0/1.1; TLS 1.2/1.3 tested.
- [ ] Valid chains/hostnames on all listeners.
- [ ] `testssl.sh` grade A or better.
- [ ] STARTTLS resets state; implicit TLS on 465/993.
- [ ] Certificate renewal automated; expiry alerts.
- [ ] MTA-STS `enforce` after a testing period.
- [ ] DANE rollover tested.

## 3. SMTP

- [ ] Open relay test fails from an external IP.
- [ ] Auth bypass test fails; sender authorization enforced.
- [ ] SMTP smuggling payloads produce one message.
- [ ] Line/size/recipient/timeout limits enforced.
- [ ] Pipelining/BDAT/8BITMIME/SMTPUTF8 tested.
- [ ] `Received`/`Return-Path`/`Delivered-To` correct.
- [ ] Null sender never bounces.
- [ ] 100-`Received` loop rejected.

## 4. Submission and access

- [ ] 587 requires TLS before AUTH; 465 works.
- [ ] SCRAM/PLAIN/OAUTHBEARER tested with real clients.
- [ ] Brute-force lockout works.
- [ ] IMAP tested with Thunderbird, Apple Mail, K-9.
- [ ] UIDVALIDITY/UIDNEXT stable across restart.
- [ ] EXPUNGE/IDLE/concurrent sessions correct.
- [ ] Quota/ACL enforced.
- [ ] JMAP session/query/get/set/blob tested (if enabled).
- [ ] Sieve upload/validate/activate tested (if enabled).
- [ ] HTTP submission API: key auth, From authorization, idempotency, limits,
      `202`/`503` behavior, status lookup, and OpenAPI contract tested.
- [ ] Webhooks: HTTPS-only, private-IP deny, HMAC, retries, replay window.

## 5. Queue and delivery

- [ ] Queue survives restart; `inflight` reclaim works.
- [ ] 4xx retries; 5xx bounces; 421 retries.
- [ ] DSN format valid; null sender; `NOTIFY`/`RET` honored.
- [ ] Double bounce goes to postmaster.
- [ ] Per-domain circuit breaker.
- [ ] Queue monitoring and alerts.
- [ ] Give-up time 4–5 days.

## 6. Authentication and deliverability

- [ ] Outbound SPF/DKIM/DMARC pass at Gmail/Yahoo/Outlook.
- [ ] DMARC aggregate reports parse.
- [ ] One-click unsubscribe works.
- [ ] FBL/complaint processing.
- [ ] rDNS/HELO correct.
- [ ] IP warmup plan approved.
- [ ] Blocklist monitoring.
- [ ] Google Postmaster/SNDS access.

## 7. Abuse

- [ ] `abuse@`/`postmaster@` monitored with SLA.
- [ ] Spam/virus filters active; `GTUBE`/`EICAR` tests pass.
- [ ] Quarantine/release workflow.
- [ ] Rate limits and account suspension runbook.
- [ ] No backscatter.
- [ ] DNSBL terms respected.

## 8. Operations

- [ ] Backups: DB PITR + blobs; restore drill passed.
- [ ] RPO/RTO documented and met.
- [ ] Dashboards and alerts live.
- [ ] Runbooks reviewed (queue, disk, cert, DNS, abuse, loop, breach).
- [ ] On-call rotation and escalation.
- [ ] Capacity plan.
- [ ] Rollback procedure tested.

## 9. Security and compliance

- [ ] Threat model reviewed.
- [ ] Penetration test or external review; no critical/high open.
- [ ] Secrets in a manager; rotation policy.
- [ ] Admin API private; MFA.
- [ ] Logs redacted; retention policy.
- [ ] Privacy policy/terms/DPA published.
- [ ] Lawful-access process documented.
- [ ] Data residency/retention approved.

## 10. Final

- [ ] Staged rollout: one domain, one IP, low volume.
- [ ] Monitoring window 24–72 h with rollback ready.
- [ ] Support/abuse contacts published.
- [ ] Status page/incident process.
- [ ] Sign-off from engineering and operations.
