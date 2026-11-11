# ADR 0005 — Durable queue: PostgreSQL transactional outbox

**Status:** Accepted
**Date:** 2026-11-11

## Context

Once an SMTP server returns `250` after `DATA`, it owns the message. The queue
must survive process crashes, DB failover, and worker restarts. It must
support per-recipient status, retries, DSNs, and inspection.

Options:

1. **In-memory `Channel<T>`:** fast, loses mail on restart. Unacceptable.
2. **Redis/RabbitMQ:** good throughput, but adds a second durability domain
   and a consistency problem with the message/DB transaction.
3. **PostgreSQL table as outbox:** transactional with message acceptance,
   queryable, no extra infrastructure.

## Decision

Use a **PostgreSQL transactional outbox**:

- Accept path: insert `messages`, `message_mailboxes`, and `queue` rows in one
  transaction; `250` only after commit.
- Workers claim with `FOR UPDATE SKIP LOCKED`, set `inflight`, heartbeat, and
  reclaim stale rows.
- One row per recipient; per-domain grouping for efficiency.
- Retry with exponential backoff + jitter; bounded give-up (4–5 days).
- DSN generation on permanent failure; null-sender messages never bounce.
- `delivery_attempts` records every try.

At-least-once delivery is explicit; idempotency/dedupe is a consumer concern.

## Consequences

Positive:

- No lost mail on process restart.
- One transaction for accept; no dual-write.
- Easy inspection and operational tooling.
- No extra broker to operate.

Negative:

- DB becomes a throughput bottleneck under very high volume.
- `SKIP LOCKED` contention at high worker counts.
- Large queue tables need partitioning/archival.

Mitigations:

- Partition `queue` by time/status when it grows.
- Keep worker batches small; index `(status, next_attempt_at)`.
- Move to a broker only if measured throughput requires it; keep the outbox
  as the source of truth.

## Alternatives considered

- **RabbitMQ/SQS:** good for fan-out, but dual-write and replay complexity.
- **Redis Streams:** fast, but durability/ack semantics need care.
- **Filesystem queue (qmail-style):** simple, but harder to query and
  transactionally couple to metadata.
