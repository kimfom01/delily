# Phase gate checklist

Use at the end of every phase in `03-implementation-roadmap.md`. A phase is
not done because code merged; it is done because these are true.

---

## 1. Scope and behavior

- [ ] The phase objective is met; no unrelated features were added.
- [ ] Public behavior is documented in the relevant `docs/`.
- [ ] New configuration options are documented with secure defaults.
- [ ] A disable/rollback switch exists for risky behavior.
- [ ] Known limitations are listed.

## 2. Build and tests

- [ ] `dotnet build -warnaserror` passes.
- [ ] `dotnet format --verify-no-changes` passes.
- [ ] Unit tests pass; new code has tests.
- [ ] Integration tests pass through the real listener.
- [ ] Conformance corpus passes.
- [ ] No new flaky tests (or they are quarantined with an issue).

## 3. Protocol correctness

- [ ] Valid input works with a real client/peer (`swaks`, `imaptest`,
  Thunderbird, Postfix, etc.).
- [ ] Invalid input returns the right code and does not crash.
- [ ] Framing/limits/timeouts are enforced.
- [ ] Reply codes/enhanced codes match the docs.
- [ ] New edge cases are in `14-edge-case-catalog.md`.

## 4. Data integrity

- [ ] Accepted messages survive a process restart.
- [ ] Crash before accept leaves no half-message.
- [ ] Duplicate delivery is handled idempotently.
- [ ] UID/UIDVALIDITY invariants hold.
- [ ] Blob/metadata consistency is checked.
- [ ] Quota/ACL enforcement is tested.

## 5. Security

- [ ] No secrets in code, logs, or tests.
- [ ] Auth required where expected; no bypass.
- [ ] Input validation on every external boundary.
- [ ] Rate limits/resource caps in place.
- [ ] TLS policy enforced; no downgrade.
- [ ] Dependency audit clean (no critical/high).
- [ ] Security tests from `13-testing-and-conformance.md` pass.

## 6. Observability

- [ ] Logs include session/queue/message IDs.
- [ ] Metrics exist for the new path.
- [ ] Errors are distinguishable from policy rejections.
- [ ] An alert exists for the failure mode that matters.
- [ ] No PII/secrets in logs.

## 7. Operations

- [ ] Backup/restore still works.
- [ ] Migration/rollback is documented.
- [ ] Runbook updated for the new failure modes.
- [ ] Capacity impact is understood.

## 8. Review

- [ ] Diff reviewed for unrelated changes.
- [ ] Commit message is conventional and scoped.
- [ ] CHANGELOG/release notes updated.
- [ ] Next phase dependencies are clear.
