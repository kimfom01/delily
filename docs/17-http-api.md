# 17 — HTTP message submission API

Delily accepts mail over SMTP **and** over HTTPS. The HTTP API is an MSA
equivalent for applications: an authenticated tenant submits a message, the
server validates and signs it, writes it to the durable queue, and returns a
message ID. Delivery is identical to SMTP submission.

This is not an open relay. It is not a public "send mail as anyone" service.
Every request is bound to a tenant, an API credential, and an authorized
sending domain.

**Normative base for the HTTP parts:** RFC 9110 (HTTP semantics), RFC 9111
(caching), RFC 9457 (Problem Details), RFC 6750 (bearer tokens), RFC 6749
(OAuth 2.0), RFC 7519/9068 (JWT), RFC 9700 (OAuth security BCP), RFC 8621
(JMAP `EmailSubmission` for the standards-based alternative).

---

## 1. Relationship to the other submission paths

| Path | Use | Auth | Standard |
|---|---|---|---|
| SMTP submission (587/465) | Mail clients | SASL | RFC 6409 |
| HTTP JSON API | Applications, web forms, backends | API key/OAuth | Delily-specific |
| HTTP raw MIME | Applications that build their own MIME | API key/OAuth | Delily-specific |
| JMAP `EmailSubmission` | JMAP clients | OAuth/session | RFC 8621 |
| SMTP port 25 | Server-to-server relay | None (policy) | RFC 5321 |

All four paths converge on the same **submission service** and **queue**. Do
not implement a second delivery path for HTTP.

---

## 2. Endpoint surface

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/v1/messages` | Send a JSON-described message |
| `POST` | `/api/v1/messages/raw` | Send a raw `message/rfc822` body |
| `GET` | `/api/v1/messages/{id}` | Delivery status and per-recipient results |
| `GET` | `/api/v1/messages/{id}/events` | Status event history (optional) |
| `POST` | `/api/v1/messages/{id}/cancel` | Cancel before delivery (optional) |
| `GET` | `/api/v1/domains` | Authorized sending domains (optional) |
| `GET` | `/api/v1/suppressions` | Bounce/complaint suppression list (optional) |

Start with the first three. The rest are operational conveniences, not
prerequisites.

---

## 3. `POST /api/v1/messages`

### Request

```http
POST /api/v1/messages HTTP/1.1
Host: api.example.com
Authorization: Bearer dlly_live_01J... 
Idempotency-Key: 018f3f7c-4b7a-7c3e-9d2a-6f1b2c3d4e5f
Content-Type: application/json

{
  "from": { "email": "billing@example.com", "name": "Example Billing" },
  "to": [
    { "email": "customer@example.net", "name": "Customer" }
  ],
  "cc": [],
  "bcc": [],
  "replyTo": { "email": "support@example.com" },
  "subject": "Your invoice",
  "text": "Your invoice is attached.",
  "html": "<p>Your invoice is attached.</p>",
  "headers": { "X-Campaign": "invoices-2026-11" },
  "attachments": [
    {
      "filename": "invoice.pdf",
      "contentType": "application/pdf",
      "contentBase64": "JVBERi0xLjQK..."
    }
  ],
  "tags": ["invoice", "billing"],
  "metadata": { "orderId": "ORD-1234" }
}
```

### Rules

- `from` is required and must be a single mailbox.
- At least one `to`, `cc`, or `bcc` recipient is required.
- `text` and/or `html` is required unless a raw MIME body is supplied.
- `bcc` recipients are envelope-only; remove the header before delivery.
- `headers` is an allowlist-controlled map; never accept `Received`,
  `Return-Path`, `Delivered-To`, `Authentication-Results`, `DKIM-Signature`,
  or `ARC-*` from the caller.
- `attachments` are decoded with a size cap; filenames are sanitized; no
  external URLs are fetched.
- `tags`/`metadata` are for search/webhooks; cap count and size.
- Unknown JSON properties are rejected (`400`) or ignored only if you
  explicitly version the schema.

### Response

```http
HTTP/1.1 202 Accepted
Content-Type: application/json
Location: /api/v1/messages/msg_01J...

{
  "id": "msg_01J...",
  "status": "queued",
  "acceptedAt": "2026-11-11T13:00:00Z"
}
```

`202` means "durably queued", not "delivered". The server must commit the
message and queue rows before returning.

---

## 4. `POST /api/v1/messages/raw`

For callers that build their own MIME (or need exact control):

```http
POST /api/v1/messages/raw HTTP/1.1
Authorization: Bearer dlly_live_...
Content-Type: message/rfc822
X-Delily-Envelope-From: billing@example.com
X-Delily-Envelope-To: customer@example.net
Idempotency-Key: ...

From: billing@example.com
To: customer@example.net
Subject: Your invoice
MIME-Version: 1.0
Content-Type: text/plain

...
```

Rules:

- The envelope headers are authoritative; do not derive them from `From:`/`To:`.
- Validate the raw message; strip/replace forbidden trace headers.
- If the caller supplies `DKIM-Signature`, verify it but re-sign with the
  tenant's selector if the tenant is the author domain.
- Enforce the same size/recipient limits as the JSON endpoint.
- Reject `message/external-body`, `message/partial` without limits, and
  malformed multipart.

An alternative is `multipart/form-data` with a `message` part and metadata
parts. Choose one and document it; do not accept both silently.

---

## 5. Status

```http
GET /api/v1/messages/msg_01J...
Authorization: Bearer dlly_live_...
```

```json
{
  "id": "msg_01J...",
  "status": "delivered",
  "recipientCount": 1,
  "acceptedAt": "2026-11-11T13:00:00Z",
  "recipients": [
    {
      "address": "customer@example.net",
      "status": "delivered",
      "attempts": 1,
      "lastAttemptAt": "2026-11-11T13:00:03Z",
      "remoteMta": "mx.example.net",
      "diagnostic": "250 2.0.0 Ok"
    }
  ]
}
```

Status values: `queued`, `deferred`, `delivered`, `bounced`, `failed`,
`cancelled`. Never expose another tenant's message; return `404`.

---

## 6. Error model

Use RFC 9457 `application/problem+json`:

```json
{
  "type": "https://delily.example/problems/sender-not-authorized",
  "title": "Sender address is not authorized",
  "status": 403,
  "detail": "billing@example.com is not in an authorized sending domain for this key.",
  "instance": "/api/v1/messages",
  "traceId": "..."
}
```

| Status | When |
|---|---|
| `400` | Malformed JSON, bad address, unsupported header, CRLF injection |
| `401` | Missing/invalid/expired credential |
| `403` | Sender/domain not authorized, scope missing, tenant suspended |
| `404` | Message/domain not found for this tenant |
| `409` | Idempotency key reused with a different payload |
| `413` | Request or attachment too large |
| `422` | Semantically invalid (no recipients, empty body) |
| `429` | Rate limit; include `Retry-After` |
| `503` | Queue/storage unavailable; do not accept |

Do not leak stack traces, SQL, internal hostnames, or whether an address
exists on another tenant.

---

## 7. Authentication

### API keys

- Format: `dlly_<env>_<public-id>_<secret>` (for example
  `dlly_live_01J..._<32-byte-base64url>`).
- Store only a hash of the secret (Argon2id or SHA-256 with a server-side
  pepper, plus a lookup by public ID). Never store or log the raw key.
- Show the key once at creation; allow rotation with an overlap window.
- Scopes: `messages:send`, `messages:read`, `domains:read`,
  `suppressions:read`, `suppressions:write`, `webhooks:manage`.
- Bind the key to a tenant and an allowed sender-domain list.
- Enforce expiry and revocation; cache validation briefly.

### OAuth 2.0 (optional)

For service-to-service or delegated access:

- Client credentials or authorization code + PKCE.
- JWT access token with `iss`, `aud`, `exp`, `scope`, `tenant`.
- Validate signature, issuer, audience, expiry, and scope.
- Use RFC 9068 token profile if you issue JWTs.
- Follow RFC 9700 for security BCP.

### Transport

- HTTPS only; redirect HTTP to HTTPS except ACME challenges.
- HSTS.
- Do not accept credentials in query strings.
- Rate-limit authentication failures.

---

## 8. Authorization and anti-abuse

- The `From` domain must be in the tenant's authorized domain list.
- The envelope sender must match the authenticated identity or an allowed
  alias.
- Reject attempts to send as another tenant or a domain you do not host.
- Do not relay to arbitrary recipients if the tenant is restricted; apply
  recipient-domain policy.
- Apply per-key and per-tenant rate limits:
  - requests/minute;
  - messages/day;
  - recipients/day;
  - attachment bytes/day.
- Return `429` with `Retry-After` and `X-RateLimit-*`.
- Suspend keys/tenants automatically on abuse signals; audit the action.
- Never expose an open relay through the API.

---

## 9. Idempotency

- `Idempotency-Key` is required for `POST` (configurable, recommended).
- Scope the key to the tenant and endpoint.
- Store: key, request hash, response status/body, expiry (24 h default).
- Same key + same request hash → return the original response (including
  `202` and message ID).
- Same key + different request hash → `409 Conflict`.
- A request without a key may be accepted, but retries can duplicate mail;
  document the risk or require the header.
- Clean up expired keys; do not let the table grow forever.

---

## 10. Build, sign, and queue flow

```
authenticate → authorize From → validate → parse attachments
  → build MIME (or accept raw) → add Date/Message-ID
  → DKIM sign with tenant selector
  → persist message + queue rows in one transaction
  → 202 Accepted
  → delivery worker (same as SMTP submission)
  → status/webhook updates
```

Rules:

- `Date` and `Message-ID` are added if missing.
- `Bcc` is removed from the stored message; recipients stay in the envelope.
- Do not add `Received` headers for HTTP submission; add a
  `Received`-equivalent trace only if your policy requires it, and never
  pretend it came from SMTP.
- Sign after all content modifications. If you add a footer, do it before
  signing.
- If signing fails, return `503` rather than sending unsigned mail.
- If the queue write fails, return `503`; do not return `202`.

---

## 11. Webhooks

Optional but expected for an API product.

Events:

- `message.accepted`
- `message.delivered`
- `message.deferred`
- `message.bounced`
- `message.failed`
- `message.complained`

Payload:

```json
{
  "id": "evt_01J...",
  "type": "message.delivered",
  "createdAt": "2026-11-11T13:00:03Z",
  "data": {
    "messageId": "msg_01J...",
    "recipient": "customer@example.net",
    "remoteMta": "mx.example.net",
    "diagnostic": "250 2.0.0 Ok"
  }
}
```

Security:

- HTTPS only; deny private/link-local/metadata IPs; re-resolve and pin.
- No redirects; short timeout; cap response body.
- Sign with HMAC-SHA256:
  `X-Delily-Signature: t=<unix>,v1=<hex>` over `<t>.<raw-body>`.
- Retry with exponential backoff; stop after a bounded period.
- Let tenants rotate the signing secret.
- Do not include full message content or credentials.

---

## 12. OpenAPI and versioning

- Publish `/openapi/v1.json` in development and a stable URL in production.
- Document schemas, auth, errors, rate limits, idempotency, and examples.
- Version in the path (`/api/v1`). Additive fields are non-breaking; removing
  or changing semantics requires `/api/v2`.
- Generate a client SDK only after the schema is stable.
- Keep the OpenAPI document in CI; fail if the checked-in contract drifts.

---

## 13. Limits

| Limit | Default |
|---|---|
| Request body | 10 MiB |
| Recipients per message | 100 |
| Attachments | 20 |
| Attachment size | 10 MiB each |
| Subject | 998 chars |
| Header count | 50 |
| Metadata/tags | 50 keys / 1 KiB each |
| Idempotency window | 24 h |
| Requests/minute/key | plan-based |
| Messages/day/tenant | plan-based |

Enforce before reading the whole body where possible (`Content-Length`,
streaming). Reject with `413` and a clear problem detail.

---

## 14. Security checklist

- [ ] HTTPS only; HSTS; no credentials in URLs.
- [ ] API keys hashed; rotation/revocation; scopes.
- [ ] Tenant isolation on every query; cross-tenant returns `404`.
- [ ] From/domain authorization; no open relay.
- [ ] Header allowlist; CRLF injection rejected.
- [ ] Attachment decoding limits; filename sanitization; no URL fetch.
- [ ] Idempotency implemented and tested.
- [ ] Rate limits and abuse suspension.
- [ ] Queue write before `202`.
- [ ] DKIM signing before queue.
- [ ] Webhook SSRF protections and HMAC.
- [ ] No message bodies/secrets in logs.
- [ ] OpenAPI contract tested in CI.
- [ ] Audit log for send/status/key changes.

---

## 15. Test cases

- Valid JSON request → `202`, message queued, delivered.
- Missing/invalid key → `401`.
- Key for tenant A sends as tenant B → `403`.
- From domain not authorized → `403`.
- No recipients → `422`.
- Oversized body/attachment → `413`.
- Header with CRLF → `400`.
- Same idempotency key + same body → same message ID, one queue row.
- Same key + different body → `409`.
- Rate limit → `429` with `Retry-After`.
- Raw MIME with a forged `Return-Path` → stripped/rejected.
- Raw MIME with a DKIM signature from another domain → verify/re-sign policy.
- Queue down → `503`, no `202`.
- Status for another tenant → `404`.
- Webhook to private IP → blocked.
- Webhook signature verifies; replay window rejected.

---

## 16. Relationship to JMAP

JMAP `EmailSubmission` (RFC 8621) is the standards-based HTTP submission
model: create an `Email`, then an `EmailSubmission`, then query its status.
If you implement JMAP, route it through the same submission service and queue.

The custom REST API exists because it is simpler for many backends and maps
naturally to "send this JSON and give me an ID". Do not implement two
independent delivery paths; share validation, signing, queueing, and status.
