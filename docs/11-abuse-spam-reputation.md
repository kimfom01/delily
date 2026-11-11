# 11 — Abuse, spam, and reputation

**Normative/operational base:** RFC 7208/6376/9989 (authentication), RFC 5965
(ARF), RFC 6650 (feedback reports), RFC 6471 (DNSBL practices), RFC 8058
(one-click unsubscribe), RFC 2369/2919 (list headers), RFC 3834
(auto-responses), RFC 2142 (role addresses), MAAWG best practices, and
receiver published bulk-sender requirements (Google/Yahoo 2024).

This is the least standardized and most operational part of mail. Treat every
score as a signal, never as ground truth.

---

## 1. Inbound pipeline

Order matters: cheap, high-signal checks first; expensive content analysis
last.

```
TCP accept
  → connection reputation (IP, ASN, HELO, rDNS)
  → DNSBL / URIBL (with timeouts)
  → SPF
  → DKIM
  → DMARC
  → ARC
  → greylisting (new triple only)
  → recipient validation
  → rate limits / tarpitting
  → content filter (rspamd/SpamAssassin)
  → virus scan (ClamAV)
  → policy decision: accept / quarantine / reject / discard
```

### Decision matrix

| Signal | Action |
|---|---|
| SPF fail + DMARC reject | Reject at SMTP (`550 5.7.1`) |
| DMARC quarantine | Deliver to Junk/quarantine |
| DNSBL hit (high confidence) | Reject or quarantine; log the list |
| DNSBL timeout | Do not block; record `temperror` |
| Greylist new triple | `451 4.7.1` with a retry hint |
| Virus | Reject or quarantine; never deliver |
| Spam score high | Quarantine; notify user digest |
| User allowlist | Override spam score, not malware |
| User blocklist | File to Junk/Trash, do not bounce |

Reject at SMTP when you are confident; accepting and bouncing creates
backscatter. If you accept, quarantine silently.

---

## 2. Connection reputation

Inputs:

- IP and /24 or /48 reputation;
- ASN and hosting/residential classification;
- forward-confirmed reverse DNS (FCrDNS);
- HELO/EHLO validity (FQDN, resolves, not an IP literal, not your own name);
- TLS presence/version (a signal, not a verdict);
- connection rate and concurrent sessions;
- historical spam/ham ratio for the IP/domain.

Do not block solely on missing rDNS; many legitimate small senders lack it.
Use it as a weight.

---

## 3. DNSBLs and URIBLs

- Query the reversed IP (`10.2.0.192.zen.spamhaus.org`) or domain.
- A `127.0.0.x` answer means listed; `127.255.255.x` often means query error
  or blocked resolver.
- Use a reputable resolver; some public resolvers are blocked by DNSBL
  operators.
- Set short timeouts (1–2 s); a timeout is not a listing.
- Do not stack dozens of lists; pick a few with different coverage and
  measure false positives.
- Respect the operator's terms (query volume, commercial use).
- Log the list and response; allow manual override.
- URIBLs check domains/URLs in the body; use after parsing, with limits.

RFC 6471 is the operational guide; read it before blocking on a list.

---

## 4. Greylisting

Greylisting temporarily rejects a new `(IP, sender, recipient)` triple with a
4xx, expecting legitimate MTAs to retry.

Rules:

- Use `451 4.7.1 Greylisted, try again later`.
- Store the triple with a first-seen time; allow after 5–15 minutes.
- Whitelist authenticated users, known forwarders, and senders that pass
  SPF/DKIM/DMARC with a strong reputation.
- Expire entries (for example 24 h) to bound storage.
- Do not greylist DSNs with a null sender; they may not retry.
- Do not greylist your own outbound or local mail.
- Measure delivery delay; greylisting is a trade-off, not a default virtue.

---

## 5. Content filtering

### Integrate, do not invent

Use rspamd (recommended) or SpamAssassin over a local socket. They provide:

- rule sets and Bayesian classification;
- URL/domain reputation;
- DKIM/SPF/DMARC checks (do not duplicate);
- fuzzy hashing;
- malware/phishing signals;
- learning and statistics.

Your server owns:

- the decision policy (thresholds, quarantine, user overrides);
- the feedback loop (spam/ham reports);
- the audit trail;
- per-tenant tuning.

### Headers

Add your verdict without destroying the original:

```
X-Spam-Score: 12.3
X-Spam-Action: quarantine
X-Spam-Status: Yes, score=12.3 required=5.0
Authentication-Results: ...
```

Never let a client-supplied `X-Spam-*` header influence your decision.

### Virus scanning

- ClamAV over a local socket.
- Scan decoded attachments, not just the raw body.
- Enforce archive limits (depth, entries, ratio, total size).
- Quarantine or reject; never deliver malware.
- Keep signatures updated; alert if the update is stale.
- `EICAR` is the safe test string; `GTUBE` is the spam test string.

---

## 6. Rate limits and resource protection

| Dimension | Example limit |
|---|---|
| Connections per IP | 20 concurrent, 100/min |
| Messages per IP | 100/hour (new), 1000/hour (known) |
| Recipients per message | 100 |
| Invalid recipients per connection | 10, then `421` |
| Auth failures per user/IP | 5 per 15 min |
| Outbound messages per user | plan-based |
| Outbound recipients per day | plan-based |
| Concurrent IMAP sessions | 10 per user |
| Sieve execution | 5 s / 100 MB per message |

Tarpitting: delay responses to dictionary attacks (for example, 5–10 s after
repeated `RCPT` failures). Do not tarpit legitimate bulk senders into
timeouts; measure.

---

## 7. Backscatter and joe jobs

- Reject unknown recipients at `RCPT`, not after `DATA`.
- Do not bounce spam/virus; quarantine or discard.
- Never bounce a null-sender message.
- Rate-limit DSNs per sender domain.
- If you accept a message and later cannot deliver, bounce only the envelope
  sender; if it is forged, you are sending backscatter. Prefer rejecting at
  SMTP when you can predict the failure.
- A "joe job" floods a victim with bounces by forging their address; a good
  DMARC policy and reject-at-edge behavior reduce it, but do not bounce
  unauthenticated mail.

---

## 8. Outbound deliverability

### Technical baseline

- Static, dedicated sending IPs with correct PTR (rDNS) matching the HELO.
- HELO/EHLO is a valid FQDN that resolves to the sending IP.
- SPF `-all` (or `~all` during rollout), DKIM signed, DMARC enforced.
- MTA-STS published and enforced; TLS-RPT enabled; DANE if DNSSEC.
- Valid TLS certificates; TLS 1.2+.
- No open relay, no compromised accounts, no snowshoe/spam.
- `List-Unsubscribe` and `List-Unsubscribe-Post` for bulk/list mail.
- `List-Id`, `Precedence`, `Auto-Submitted` where appropriate.
- Role addresses (`postmaster`, `abuse`) monitored.

### Google/Yahoo bulk sender requirements (2024)

For bulk senders (roughly 5,000+ messages/day to Gmail/Yahoo):

- SPF and DKIM aligned with the From domain;
- DMARC record (`p=none` minimum; `p=quarantine/reject` recommended);
- one-click unsubscribe (`List-Unsubscribe-Post`) for marketing/list mail;
- spam complaint rate below 0.3% (Google Postmaster Tools);
- no impersonation; From domain aligned.

These are receiver policies, not RFCs. They change; check the current
documentation before a campaign.

### IP warmup

1. Start with low volume to engaged recipients.
2. Increase gradually (for example double every few days) while monitoring
   deferrals, complaints, and blocklists.
3. Segment by domain and engagement; do not blast a cold list.
4. Keep a stable IP/domain reputation; do not rotate IPs to evade filters.
5. Pause and investigate if complaint rate or 4xx rate spikes.

### List hygiene

- Confirmed opt-in; no purchased lists.
- Bounce processing: hard bounce → suppress; soft bounce → retry then
  suppress.
- Complaint feedback loop (FBL) → suppress.
- Sunset inactive recipients.
- One-click unsubscribe honored within 48 h (ideally immediately).
- `List-Unsubscribe` mailto and/or URL; `List-Unsubscribe-Post:
  List-Unsubscribe=One-Click`.

### Monitoring

| Source | What |
|---|---|
| Google Postmaster Tools | Reputation, spam rate, delivery errors |
| Microsoft SNDS | IP reputation, complaint rates |
| Yahoo FBL | Complaints |
| DMARC aggregate reports | Auth/alignment failures |
| TLS-RPT | TLS delivery failures |
| DNSBL checks | Spamhaus, Barracuda, etc. |
| Seed lists | Inbox vs spam placement |
| SMTP response codes | 4xx/5xx by domain |

Build a dashboard and alert on changes, not absolutes.

---

## 9. Feedback loops and ARF

- Register for FBLs where available; parse ARF (RFC 5965) reports.
- A complaint is a user action; suppress the recipient immediately unless the
  user explicitly re-subscribes.
- Do not auto-reply to complaints.
- Keep complaint rate per domain/campaign; alert above 0.1–0.3%.
- ARF reports can contain message content; handle under privacy policy.

---

## 10. Abuse handling

- Publish `abuse@` and `postmaster@` (RFC 2142) and respond within a defined
  SLA.
- Accept abuse reports via email and a web form; include headers.
- Investigate outbound spam/compromise; suspend accounts per policy.
- Keep an audit log of enforcement actions.
- Provide appeals.
- Cooperate with upstream providers and blocklists; delist after remediation.
- Do not disclose user data without legal process; follow privacy law.

---

## 11. BIMI (optional)

BIMI displays a brand logo in supporting clients. It requires:

- DMARC enforcement (`p=quarantine` or `reject`);
- a Verified Mark Certificate (VMC) or Common Mark Certificate (CMC);
- a `default._bimi` TXT record with the logo URL and certificate;
- SVG Tiny PS logo.

Do not make BIMI a launch requirement. It is a branding feature, not a
deliverability fix.

---

## 12. Test plan

- `GTUBE` message is quarantined.
- `EICAR` attachment is blocked.
- A normal message from a reputable sender passes.
- A forwarded message with broken SPF but valid DKIM passes DMARC.
- A DMARC `p=reject` spoof is rejected at SMTP.
- A greylisted triple is retried and accepted.
- A DNSBL timeout does not block mail.
- A dictionary attack triggers tarpitting/421.
- A hard bounce suppresses the recipient.
- A complaint suppresses the recipient.
- One-click unsubscribe works.
- Outbound mail passes Gmail/Yahoo authentication checks.
- Blocklist monitoring alerts on a listing.
- A compromised account is rate-limited and suspended per policy.

---

## 13. Policy checklist

- [ ] Published abuse/postmaster contacts.
- [ ] Acceptable-use policy and enforcement runbook.
- [ ] Per-user/domain rate limits.
- [ ] Quarantine with user release.
- [ ] Spam/ham feedback stored and used.
- [ ] DNSBL provider terms respected.
- [ ] No backscatter for spam/virus.
- [ ] Outbound authentication aligned.
- [ ] FBL/complaint processing.
- [ ] IP warmup plan.
- [ ] Blocklist monitoring and delisting runbook.
- [ ] Privacy review of content scanning and retention.

---

## 14. Sources for receiver policies

These are operational requirements, not RFCs, and they change:

- Google Workspace email sender guidelines:
  <https://support.google.com/a/answer/81126>
- Yahoo sender best practices:
  <https://senders.yahooinc.com/best-practices/>
- Microsoft SNDS / JMRP:
  <https://sendersupport.olc.protection.outlook.com/snds/>
- MAAWG best practices:
  <https://www.m3aawg.org/>
- Spamhaus:
  <https://www.spamhaus.org/>
- Google Postmaster Tools:
  <https://postmaster.google.com/>

Re-check these before a bulk campaign or a new IP warmup. The 5,000/day
threshold and complaint-rate targets are receiver policy, not protocol
constants.
