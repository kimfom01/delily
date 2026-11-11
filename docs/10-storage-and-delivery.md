# 10 — Storage and delivery

**Normative base:** RFC 9051 (IMAP4rev2), RFC 3501 (rev1), RFC 4315
(UIDPLUS), RFC 7162 (CONDSTORE/QRESYNC), RFC 9208 (QUOTA), RFC 4314 (ACL),
RFC 5228 (Sieve), RFC 2033 (LMTP), RFC 2045–2049 (MIME).

This is where messages become mailboxes. Get the invariants right; everything
else (IMAP, JMAP, search, backups) depends on them.

---

## 1. Core model

```
Tenant
  └─ Domain
       ├─ User ── Credentials, Identities
       ├─ Alias ──▶ destination (user, address, or group)
       └─ Mailbox ── hierarchical name, UIDVALIDITY, UIDNEXT, HIGHESTMODSEQ
            └─ Message membership ── UID, flags, keywords, INTERNALDATE, MODSEQ
                 └─ Message ── immutable blob (raw RFC 5322 bytes)
```

Key separation:

- **Message** = immutable content + envelope metadata.
- **Membership** = a message in a mailbox, with a UID and mutable flags.
- **Mailbox** = a named container owned by a user (or shared).
- **User** = an authenticated principal with one or more addresses.

A message can be in several mailboxes (INBOX + Archive) without duplicating
the blob. Each membership has its own UID and flags.

---

## 2. Storage layout

### Recommended: hybrid

| Data | Store |
|---|---|
| Raw message bytes | Content-addressed blob store (filesystem/S3) |
| Envelope, headers index, parsed metadata | PostgreSQL |
| Mailbox membership, flags, UIDs, MODSEQ | PostgreSQL |
| Full-text index | PostgreSQL `tsvector` (or external later) |
| Search attachments | Object store + extracted text table |
| Queue | PostgreSQL |
| Sessions/rate limits | In-memory/Redis (not durable state) |

### Blob path

```
data/blobs/sha256/ab/cd/abcdef...   # raw bytes
data/tmp/<uuid>.eml                 # staging
```

Write with `O_TMPFILE`/temp + `fsync` + atomic rename. Never expose a
client-supplied filename as a path.

### Why not Maildir only?

Maildir is excellent for a single-node, Unix-style MDA:

```
Maildir/
  tmp/
  new/
  cur/
  .Sent/
    cur/
```

It is simple and crash-safe (write temp, rename to `new`). But:

- flags live in filenames and are awkward to query;
- UIDs, MODSEQ, and quotas need a separate index;
- shared mailboxes and ACLs are harder;
- object storage is awkward.

If you want Maildir compatibility, use it as an **export/import format** and
keep the authoritative metadata in PostgreSQL. Do not make filename parsing
your IMAP model.

### Why not mbox?

mbox is a single file with `From ` separators. It has locking problems,
painful random access, and corruption risk. Do not use it for a new server.

### Why not blobs in PostgreSQL?

`bytea` works for small deployments but bloats WAL, backups, and memory. Keep
blobs outside the DB; store the hash and size.

---

## 3. UID and sequence invariants

| Concept | Rule |
|---|---|
| `UIDVALIDITY` | Per mailbox; must not change unless the mailbox is deleted and recreated with a new value. Clients cache by it. |
| `UID` | 32-bit nonzero, strictly increasing within a mailbox, never reused. |
| `UIDNEXT` | Next UID to assign; never decreases. |
| Sequence number | 1..N in the selected mailbox; changes after `EXPUNGE`; never stored. |
| `MODSEQ` | Per mailbox; increases on every flag/mailbox change. |
| `HIGHESTMODSEQ` | Current max modseq; used by CONDSTORE/QRESYNC. |
| `INTERNALDATE` | Server receipt time; not the `Date:` header. |
| `\Recent` | Session-scoped; only one session should see it. |

Allocate UIDs inside the same transaction that inserts the membership:

```sql
UPDATE mailboxes
SET uidnext = uidnext + 1
WHERE id = $1
RETURNING uidnext - 1 AS uid;
```

Never allocate a UID and then fail to insert the membership without rolling
back; gaps are allowed, reuse is not.

### UIDVALIDITY changes

Only change it when:

- the mailbox is deleted and recreated;
- the underlying message set is rebuilt in a way that invalidates old UIDs
  (for example, a migration that cannot preserve them).

If you change it, clients must discard their cache and resynchronize. Warn in
the release notes; never change it silently.

---

## 4. Flags and keywords

System flags:

| Flag | Meaning |
|---|---|
| `\Seen` | Read |
| `\Answered` | Replied |
| `\Flagged` | Important |
| `\Deleted` | Marked for deletion |
| `\Draft` | Draft |
| `\Recent` | New to this session (not persistent) |

Store system flags as a bitmask. Store keywords in a normalized table or
`text[]`; enforce a per-message and per-mailbox limit (for example 100
keywords, 64 chars each, no control characters).

`PERMANENTFLAGS` should include `\*` only if you allow arbitrary keywords.
If you do, sanitize them and cap the count.

---

## 5. Local delivery pipeline

```
SMTP accept
  → authenticate/spam/virus
  → route (user/alias/group)
  → for each recipient:
       check quota
       apply Sieve
       select mailbox (INBOX or rule target)
       insert membership (UID, flags, INTERNALDATE)
       update mailbox counters
       notify IMAP IDLE / JMAP push
  → commit
  → 250
```

### Ordering

1. Resolve recipients to local users/aliases.
2. Expand groups/lists with a loop guard.
3. Check per-user and per-domain quotas before writing.
4. Run Sieve in a sandbox with a timeout; it may file/copy/redirect.
5. Insert memberships in a transaction per mailbox (or per user).
6. Commit; then notify.
7. If any recipient fails permanently, record it; do not fail the whole
   message after accepting it unless you can bounce safely.

### Duplicate handling

- At-least-once delivery can duplicate. Use a dedupe key:
  `(mailbox_id, message_hash, envelope_from, envelope_to, received_date_bucket)`.
- Do not dedupe by `Message-ID` alone; legitimate duplicates exist.
- Keep a short dedupe window (minutes), not forever.

### Quota

- Check `used_bytes + message_size <= quota_bytes` and
  `used_messages + 1 <= quota_messages`.
- On failure, return `452 4.2.2` at SMTP if possible; for IMAP `APPEND`,
  `NO [OVERQUOTA]`.
- Update usage transactionally; recompute periodically to correct drift.
- Count blob size once per message, not per membership, but quota is usually
  per user; decide whether shared blobs count once or per membership.

---

## 6. IMAP-specific storage behavior

### `APPEND`

- Parse flags and internal date.
- Enforce max message size and quota.
- Ignore `\Recent` from the client.
- Assign the next UID.
- Return `APPENDUID <uidvalidity> <uid>` if `UIDPLUS`.

### `COPY` / `MOVE`

- Copy membership rows, assign new UIDs in the destination.
- Preserve flags, keywords, and internal date (unless the client requests
  otherwise).
- `COPY` to the same mailbox creates new UIDs.
- `MOVE` must be atomic from the client's perspective; use `COPYUID` and
  `EXPUNGE` semantics (RFC 6851).
- If the destination is over quota, fail the whole operation; do not leave a
  partial copy.

### `EXPUNGE`

- Remove `\Deleted` memberships in ascending sequence order.
- Send untagged `* n EXPUNGE` in the order the client sees them; adjust
  sequence numbers as you go.
- `UID EXPUNGE` only removes the specified UIDs.
- `CLOSE` expunges; `UNSELECT` does not.
- Do not delete the blob until no memberships reference it (GC).

### `STORE`

- `FLAGS`, `+FLAGS`, `-FLAGS`, with optional `.SILENT`.
- Update `MODSEQ`; send untagged `FETCH` unless silent.
- A flag change on an expunged message is `NO`.
- Concurrent stores: last writer wins for the flag set; use row locks or
  optimistic concurrency.

### `SEARCH`

- Run against the index, not by parsing every blob.
- Support `CHARSET UTF-8`; return `NO [BADCHARSET]` otherwise.
- Bound the result set; use `MESSAGELIMIT`/`PARTIAL`.
- Date comparisons use `INTERNALDATE` for `SINCE`/`BEFORE` and the `Date:`
  header for `SENTSINCE`/`SENTBEFORE`.

### `IDLE`

- Hold a lightweight notification subscription, not a DB transaction.
- Send `+ idling`; wait for `DONE` or a change.
- On change, send `* n EXISTS`, `* n RECENT`, `* n EXPUNGE`, or
  `* OK [HIGHESTMODSEQ ...]` as appropriate.
- Enforce a maximum idle time; send `* BYE` or `* OK` and close.

---

## 7. Threading

Use `Message-ID`, `In-Reply-To`, and `References`:

1. Normalize Message-IDs (strip whitespace, keep angle brackets).
2. Build a graph: message → parent (last `References` entry or `In-Reply-To`).
3. Handle missing parents by creating a placeholder or attaching to the root.
4. Detect cycles; break them deterministically.
5. Fall back to subject only as a last resort, never as the primary key.
6. Recompute affected threads when a message is added/moved/deleted.

Store a `thread_id` on the message/membership for fast `THREAD`/JMAP
`Thread/get`. Keep the algorithm version so you can rebuild.

---

## 8. Search and indexing

Start with PostgreSQL:

```sql
ALTER TABLE messages ADD COLUMN search_vector tsvector;
CREATE INDEX messages_search_idx ON messages USING GIN (search_vector);
```

- Index headers (From/To/Cc/Subject) and extracted text body.
- Do not index attachments by default; add an opt-in extracted-text table.
- Use language configuration per user/domain; fall back to `simple`.
- Update incrementally; batch reindex jobs.
- Cap indexed text (for example 1 MiB per message) to avoid bloat.
- Search must respect ACLs and tenant boundaries; never return another user's
  message.

---

## 9. Quotas (RFC 9208)

- Advertise `QUOTA`, `QUOTAROOT`.
- `GETQUOTA "user/user@example.com"` returns `STORAGE` and `MESSAGE`.
- `SETQUOTA` only for admins.
- Enforce at `APPEND`, `COPY`, delivery, and Sieve `fileinto`.
- `NO [OVERQUOTA]` for IMAP; `452 4.2.2` for SMTP.
- Recompute usage nightly; alert on drift.

---

## 10. ACL and sharing (RFC 4314, JMAP Sharing)

- Rights: `l` lookup, `r` read, `s` seen, `w` write, `i` insert, `p` post,
  `k` create mailbox, `x` delete mailbox, `t` delete messages, `e` expunge,
  `a` administer.
- Check rights on every command; do not rely on UI.
- `MYRIGHTS`, `GETACL`, `SETACL`, `DELETEACL`, `LISTRIGHTS`.
- JMAP Sharing (RFC 9670) maps shared mailboxes to accounts; enforce the same
  rights.
- Cross-tenant sharing is a policy decision; default deny.

---

## 11. Backups and recovery

### What to back up

- PostgreSQL: base backup + WAL archive (PITR).
- Blob store: snapshots or object-store versioning.
- Configuration/secrets: encrypted backup, separate from data.
- DKIM private keys: backed up securely; losing them breaks signing.
- TLS keys/certs: can be reissued, but back up account keys.

### Consistency

- A DB backup and blob snapshot must be from compatible points in time.
- If a blob is missing, the message is corrupt; if an extra blob exists, GC
  will remove it.
- Run a scrub: for every membership, verify the blob exists and hashes
  correctly; for every blob, verify at least one reference (or mark orphan).

### Targets

| Metric | Target |
|---|---|
| RPO | ≤ 15 min (WAL) |
| RTO | ≤ 1 h for a single node |
| Restore test | Monthly |
| Blob scrub | Weekly |
| DB integrity check | Weekly |

### Restore drill

1. Provision a clean environment.
2. Restore the DB to a timestamp.
3. Mount/restore the blob store at the same timestamp.
4. Start in read-only/maintenance mode.
5. Run scrub and sample message fetches.
6. Switch traffic; verify IMAP/JMAP and delivery.

---

## 12. Data lifecycle

| Data | Retention |
|---|---|
| Delivered messages | User-controlled; until deleted/expunged |
| Trash | 30 days (configurable) |
| Spam/quarantine | 30 days |
| Queue | Until delivered/bounced; DSN after give-up |
| Delivery logs | 30–90 days (privacy review) |
| Auth logs | 90 days |
| DMARC/TLS-RPT reports | 90 days–1 year |
| Backups | 30–90 days |
| Audit log | 1 year (policy) |

Implement:

- user deletion with a grace period;
- GDPR erasure (including backups, within policy);
- legal hold (exempt from deletion);
- export (IMAP/JMAP/Maildir) before deletion;
- tombstone records for audit.

---

## 13. Migration and import

- **IMAP import:** `APPEND` with flags and internal date; use `UIDPLUS` to
  learn the new UID; batch with `MULTIAPPEND`.
- **Maildir import:** read `cur/` flags from the filename suffix
  (`:2,S`), `new/` as recent, `tmp/` ignored.
- **mbox import:** split on `From ` lines, unescape `>From `, preserve
  `Status`/`X-Status` flags.
- **Dedupe:** by hash + mailbox + internal date; do not collapse legitimate
  duplicates blindly.
- **UID mapping:** keep a table of old→new UIDs if clients will resync.
- **Internal dates:** preserve if available; otherwise use file mtime or now.
- **Failure handling:** per-message errors do not abort the whole import;
  report a manifest.

---

## 14. Integrity and GC

- Content hash: SHA-256 over the raw bytes.
- On read, optionally verify hash; on scrub, always.
- Refcount or reference table for blobs; GC only when count = 0 and grace
  period passed.
- Orphan detection: blob with no membership; membership with no blob.
- Tombstones for deleted messages if audit/legal hold requires.
- Alert on hash mismatch, missing blob, or orphan spike.

---

## 15. Storage checklist

- [ ] Raw bytes preserved; hash recorded.
- [ ] UIDVALIDITY stable; UIDNEXT monotonic; UIDs never reused.
- [ ] Flags/keywords persisted and indexed.
- [ ] `\Recent` session-scoped.
- [ ] Expunge does not delete shared blobs prematurely.
- [ ] Quota enforced at delivery/APPEND/COPY.
- [ ] ACL checked on every command.
- [ ] Search respects tenant/ACL.
- [ ] Backup/restore tested; scrub passes.
- [ ] Import preserves flags/dates and reports failures.
- [ ] GC has a grace period and an audit trail.
