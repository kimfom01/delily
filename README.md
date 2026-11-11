# delily
An e-mail server for you

## Documentation

The research and implementation knowledge base is in [`docs/`](docs/README.md):

- [Verified RFC map](docs/01-rfc-map.md) — what is current, obsolete, or still a draft
- [Architecture](docs/02-architecture.md) — processes, storage, queue, data flow
- [Implementation roadmap](docs/03-implementation-roadmap.md) — phases, exit gates, first 10 days
- [Edge-case catalog](docs/14-edge-case-catalog.md) — expected behavior for awkward inputs

Read `docs/README.md` for the full index and reading order.

The server accepts mail over SMTP **and** an authenticated HTTPS API at
`/api/v1/messages` (contract: [`docs/17-http-api.md`](docs/17-http-api.md)).
The endpoint is scaffolded now and implemented in Phase 6b.

## Build and run

```bash
dotnet restore
dotnet build
dotnet test

# Local dependencies (PostgreSQL + Mailpit)
docker compose up -d

# HTTP host (health/admin/JMAP later); SMTP/IMAP listeners start in Phase 1+
dotnet run --project src/Delily.Host
curl http://localhost:5105/health/live
```

The development SMTP port is reserved as `2525` to avoid requiring root; the
listener itself starts in Phase 1. The production defaults are 25/587/465.
See `src/Delily.Host/appsettings.json`.
