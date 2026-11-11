# ADR 0003 — Storage engine: PostgreSQL metadata + content-addressed blobs

**Status:** Accepted
**Date:** 2026-11-11

## Context

Messages are large immutable blobs; mailbox state (UIDs, flags, modseq,
quotas, ACLs) is small, mutable, and transactional. A single storage engine
that does both well is rare.

Options:

1. **Maildir only:** simple, crash-safe, but flags/UIDs/quotas/search require
   a separate index; shared mailboxes and object storage are awkward.
2. **mbox:** single-file, locking and corruption problems; reject.
3. **All blobs in PostgreSQL (`bytea`):** transactional, but bloats WAL,
   backups, and memory; expensive for large attachments.
4. **PostgreSQL metadata + filesystem/object-store blobs:** best fit.

## Decision

Use:

- **PostgreSQL** for users, domains, mailboxes, memberships, flags, UIDs,
  MODSEQ, quotas, queue, attempts, and search index.
- **Content-addressed blob store** on the filesystem (`sha256/ab/cd/...`) for
  raw RFC 5322 bytes; S3-compatible object storage later behind the same
  interface.
- **PostgreSQL `tsvector`** for full-text search initially.

Write path: temp file → hash + fsync → atomic rename → DB transaction →
`250`. GC removes unreferenced blobs after a grace period.

## Consequences

Positive:

- Transactions for mailbox state; blobs are simple and debuggable.
- Cheap dedupe of identical messages.
- Backups: PITR for DB, snapshots/versioning for blobs.
- Scales to object storage without changing the data model.

Negative:

- Two stores must be kept consistent; orphan/missing blob handling is
  required.
- Cross-store backup/restore needs a consistent point in time.
- Dedupe interacts with per-tenant encryption (decide explicitly).

Mitigations:

- Weekly scrub: membership↔blob, hash verification.
- GC with grace period and audit.
- Back up DB + blobs at the same timestamp; test restores monthly.
- If per-tenant encryption is required, disable cross-tenant dedupe and store
  a per-tenant encrypted blob.

## Alternatives considered

- **SQLite:** fine for tests/single-user, weak for concurrent multi-user
  workloads and HA.
- **Maildir as source of truth:** viable for a small MDA, but not for
  IMAP/JMAP semantics at scale.
- **S3-only with metadata in object tags:** too limited for queries and
  transactions.
