# Freight Ops

[![CI](https://github.com/poker-kid-100717/freight-ops/actions/workflows/ci.yml/badge.svg)](https://github.com/poker-kid-100717/freight-ops/actions/workflows/ci.yml)

A clean-room portfolio freight CRM: customer accounts, an explainable opportunity queue, an operational dashboard, and read-only external load visibility.

It is the standalone version of Freight Ops from [logistics-portfolio-suite](https://github.com/poker-kid-100717/logistics-portfolio-suite). It is a companion to [ltl-planner](https://github.com/poker-kid-100717/ltl-planner) and [yard-ops](https://github.com/poker-kid-100717/yard-ops), covering the customer side of the same freight workflow, and runs entirely on its own.

## What it does

A working CRM, not a mock-up. Everything below reads and writes a real database.

| Menu | Pages |
| --- | --- |
| **Home** | Dashboard (book-of-business numbers and the accounts to work next) · My Day (overdue, due-today and upcoming follow-ups, open quotes, recent activity for the selected rep) |
| **Sales** | Leads (qualify, then convert into an account and primary contact) · Accounts (search, filter, sort by priority score) and account detail (contacts, activity timeline, follow-ups, quotes, lanes) · Contacts · Pipeline (quote board) · Quotes |
| **Operations** | Lanes (target rates per account; quote a lane in one click) · Loads (read-only TMS load visibility) · Carriers (MC numbers, equipment, rating, status) |
| **Insights** | Reports (revenue by customer, quotes by month, activity by rep, accounts by stage; each exports to CSV) · Activity Log |

Settings sits at the bottom of the menu. The top bar has global search (`/`), a **+ New** menu for every record type, and a "viewing as" rep selector.

Rules the API enforces: quotes move Draft → Sent → Won/Lost only; a prospect's first won quote makes it Active; only qualified leads convert, in one transaction; logging activity resets "days since last touch"; one primary contact per account; MC numbers and account names are unique.

## Demonstrates

- .NET 10 minimal API with EF Core 10 on PostgreSQL (migrations applied by the deploy pipeline as the owner; the running app connects as a least-privilege role), validation as ProblemDetails, and 409s for rule violations
- explainable opportunity scoring computed from live data, with a reason and next step for every account
- Angular 22 routed app: lazy-loaded pages, signals, one accessible drawer for every create/edit form, light and dark themes, phone-width layout
- a public-demo posture: per-client write rate limits, request size limits, and a daily data reset from a Cloudflare cron trigger
- optional read-only Alvys Loads Search through an OAuth 2.0 client-credentials adapter
- integration tests against both SQLite and PostgreSQL in CI
- Cloudflare Workers + Containers hosting deployed from GitHub Actions

The scoring formula is intentionally portfolio-only and is not copied from a professional system. See [docs/architecture.md](docs/architecture.md).

## Run locally

```bash
cp .env.example .env
docker compose up --build
```

Compose starts PostgreSQL too; the API applies migrations and seeds fictional demo data on first start.

- UI: http://localhost:4201
- API: http://localhost:5101 (health at `/health`)

Without Docker:

```bash
ASPNETCORE_URLS=http://localhost:5101 dotnet run --project api   # no DATABASE_URL: throwaway SQLite demo store
cd web && npm install && npm start   # UI on http://localhost:4201; /api proxies to :5101
```

Demo mode is the default and needs no credentials. To enable live, read-only Alvys reads set `ALVYS_MODE=Live`, `ALVYS_CLIENT_ID`, and `ALVYS_CLIENT_SECRET`. Credentials stay server-side; the Angular app never sees them.

## Tests

```bash
dotnet test tests/Portfolio.Freight.Api.Tests.csproj                                   # SQLite
TEST_DATABASE_URL=postgres://user:pass@localhost:5432/freight_test dotnet test tests/Portfolio.Freight.Api.Tests.csproj  # PostgreSQL
```

The integration tests start the real API and cover every workflow and rule above.

## Deploy to Cloudflare

Deployment runs from GitHub Actions on every push to `main` (`.github/workflows/deploy-cloudflare.yml`). It builds the Angular app, deploys a Worker that serves it from the edge, runs the .NET API in a Cloudflare Container, and smoke-tests the result.

Repository **secrets**:

| Secret | Required | Purpose |
| --- | --- | --- |
| `CLOUDFLARE_API_TOKEN` | yes | Wrangler deploys |
| `CLOUDFLARE_ACCOUNT_ID` | yes | Wrangler deploys |
| `DATABASE_URL` | recommended | PostgreSQL URL the running app uses: a Neon **pooled** URL ending in `?sslmode=require`, for a role with data rights only (see below). Without it the app runs on a demo database that resets whenever the container restarts. |
| `DATABASE_URL_UNPOOLED` | recommended | The owner's **direct** (non-pooled) URL. The deploy applies migrations with it before the new container starts. Without it the app migrates itself on startup (and then needs DDL rights). |
| `DEMO_RESET_TOKEN` | no | Token for `POST /api/admin/reset-demo`. The demo data resets nightly at 08:17 UTC; a per-deploy token is generated when unset. |
| `ALVYS_CLIENT_ID` / `ALVYS_CLIENT_SECRET` | no | Live, read-only Alvys mode |

Database roles (Neon or any PostgreSQL): the owner role in `DATABASE_URL_UNPOOLED` owns the schema; the app role in `DATABASE_URL` only reads and writes rows:

```sql
CREATE ROLE freight_app LOGIN PASSWORD '...';
GRANT CONNECT ON DATABASE freight TO freight_app;
GRANT USAGE ON SCHEMA public TO freight_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO freight_app;
ALTER DEFAULT PRIVILEGES FOR ROLE freight_owner IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO freight_app;
ALTER DEFAULT PRIVILEGES FOR ROLE freight_owner IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO freight_app;
```

Cloudflare cron triggers handle the nightly reset and a keep-warm ping to `/health/ready` every five minutes during weekday business hours (14:00-23:55 UTC), so the first visitor does not wait for a cold container and a suspended database.

Repository **variable** (optional):

| Variable | Purpose |
| --- | --- |
| `APP_HOST` | Custom domain such as `freight.example.com`. When unset the app is served from its `workers.dev` URL. |

Until the Cloudflare secrets exist the deploy job skips cleanly. `scripts/cloudflare-deploy.sh` can also be run locally with the same environment variables.

## Repository structure

```text
api/          .NET 10 API: Data/ (EF Core model, migrations, demo seed), Endpoints/, scoring, Alvys adapter
tests/        xUnit unit and integration tests
web/          Angular 22 UI
cloudflare/   Worker + Container definition for Cloudflare hosting
scripts/      deploy and publication-safety scripts
docs/         architecture, Alvys public API notes, clean-room boundary
```

## Clean-room boundary

Written from generic workflow requirements and public vendor documentation. It contains no former-employer source code, data, credentials, internal URLs, customer names, or company-specific business rules. See [docs/clean-room-boundary.md](docs/clean-room-boundary.md) and [NOTICE.md](NOTICE.md).

## License

MIT
