# Conformance corpus

Golden inputs and expected outcomes live here. Keep it free of real user data,
credentials, and personal information.

Planned layout:

```
smtp/
  commands/valid/
  commands/invalid/
  data/dot-stuffing/
  data/smuggling/
mime/
  simple/
  multipart/
  malformed/
  eai/
auth/
  spf/
  dkim/
  dmarc/
  arc/
imap/
  commands/
  literals/
```

Each case should have an expected output or a defect list. The
`Delily.ConformanceTests` project will grow into the runner; for now it guards
that the corpus directory exists.
