# Freight Ops

[![CI](https://github.com/poker-kid-100717/freight-ops/actions/workflows/ci.yml/badge.svg)](https://github.com/poker-kid-100717/freight-ops/actions/workflows/ci.yml)

A clean-room portfolio freight CRM: customer accounts, an explainable opportunity queue, an operational dashboard, and read-only external load visibility.

It is the standalone version of Freight Ops from [logistics-portfolio-suite](https://github.com/poker-kid-100717/logistics-portfolio-suite). It is a companion to [ltl-planner](https://github.com/poker-kid-100717/ltl-planner) and [yard-ops](https://github.com/poker-kid-100717/yard-ops), covering the customer side of the same freight workflow, and runs entirely on its own.

## Demonstrates

- explainable account/opportunity scoring with a reason and next action for every account
- Angular 22 operational dashboard
- .NET 10 minimal API
- optional read-only Alvys Loads Search through an OAuth 2.0 client-credentials adapter
- a third-party adapter boundary that degrades to synthetic data
- Cloudflare Workers + Containers hosting deployed from GitHub Actions

The scoring formula is intentionally portfolio-only and is not copied from a professional system. See [docs/architecture.md](docs/architecture.md).

## Run locally

```bash
cp .env.example .env
docker compose up --build
```

- UI: http://localhost:4201
- API: http://localhost:5101 (health at `/health`)

Without Docker:

```bash
ASPNETCORE_URLS=http://localhost:5101 dotnet run --project api
cd web && npm install && npm start   # UI on http://localhost:4201; /api proxies to :5101
```

Demo mode is the default and needs no credentials. To enable live, read-only Alvys reads set `ALVYS_MODE=Live`, `ALVYS_CLIENT_ID`, and `ALVYS_CLIENT_SECRET`. Credentials stay server-side; the Angular app never sees them.

## Tests

```bash
dotnet test tests/Portfolio.Freight.Api.Tests.csproj
```

## Deploy to Cloudflare

Deployment runs from GitHub Actions on every push to `main` (`.github/workflows/deploy-cloudflare.yml`). It builds the Angular app, deploys a Worker that serves it from the edge, runs the .NET API in a Cloudflare Container, and smoke-tests the result.

Repository **secrets**:

| Secret | Required | Purpose |
| --- | --- | --- |
| `CLOUDFLARE_API_TOKEN` | yes | Wrangler deploys |
| `CLOUDFLARE_ACCOUNT_ID` | yes | Wrangler deploys |
| `ALVYS_CLIENT_ID` / `ALVYS_CLIENT_SECRET` | no | Live, read-only Alvys mode |

Repository **variable** (optional):

| Variable | Purpose |
| --- | --- |
| `APP_HOST` | Custom domain such as `freight.example.com`. When unset the app is served from its `workers.dev` URL. |

Until the Cloudflare secrets exist the deploy job skips cleanly. `scripts/cloudflare-deploy.sh` can also be run locally with the same environment variables.

## Repository structure

```text
api/          .NET 10 API (accounts, opportunity scoring, Alvys adapter)
tests/        xUnit tests for the opportunity scorer
web/          Angular 22 UI
cloudflare/   Worker + Container definition for Cloudflare hosting
scripts/      deploy and publication-safety scripts
docs/         architecture, Alvys public API notes, clean-room boundary
```

## Clean-room boundary

Written from generic workflow requirements and public vendor documentation. It contains no former-employer source code, data, credentials, internal URLs, customer names, or company-specific business rules. See [docs/clean-room-boundary.md](docs/clean-room-boundary.md) and [NOTICE.md](NOTICE.md).

## License

MIT
