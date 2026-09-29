#!/usr/bin/env bash
# Builds the Angular app, renders the Wrangler config, and deploys the Worker +
# .NET Container to Cloudflare. Used by GitHub Actions; also runnable locally.
#
# Required (unless VALIDATE_ONLY=true): CLOUDFLARE_API_TOKEN, CLOUDFLARE_ACCOUNT_ID
# Optional:
#   APP_HOST              custom hostname (for example freight.example.com). When empty the
#                         Worker is served from its workers.dev URL only.
#   ALVYS_CLIENT_ID / ALVYS_CLIENT_SECRET  enable live, read-only Alvys mode.
#   VALIDATE_ONLY=true    build and run `wrangler deploy --dry-run` without contacting Cloudflare.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CF="$ROOT/cloudflare"
VALIDATE_ONLY="${VALIDATE_ONLY:-false}"
APP_HOST="${APP_HOST:-}"

if [ "$VALIDATE_ONLY" != "true" ]; then
  : "${CLOUDFLARE_API_TOKEN:?CLOUDFLARE_API_TOKEN is required}"
  : "${CLOUDFLARE_ACCOUNT_ID:?CLOUDFLARE_ACCOUNT_ID is required}"
fi
export APP_HOST

echo "==> Building Angular app"
npm install --prefix "$ROOT/web"
npm run build --prefix "$ROOT/web"

echo "==> Preparing Cloudflare Worker"
npm install --prefix "$CF"

node - "$CF/wrangler.template.jsonc" "$CF/wrangler.generated.jsonc" <<'NODE'
const fs = require("fs");
const [template, output] = process.argv.slice(2);
const config = JSON.parse(fs.readFileSync(template, "utf8"));
if (process.env.APP_HOST) {
  config.routes = [{ pattern: process.env.APP_HOST, custom_domain: true }];
}
fs.writeFileSync(output, JSON.stringify(config, null, 2) + "\n");
NODE

# Freight Ops has no required secrets; Alvys credentials are uploaded only when both are set.
SECRETS_ARGS=()
if [ -n "${ALVYS_CLIENT_ID:-}" ] && [ -n "${ALVYS_CLIENT_SECRET:-}" ]; then
  SECRETS_FILE="$(mktemp)"
  trap 'rm -f "$SECRETS_FILE"' EXIT
  chmod 600 "$SECRETS_FILE"
  node > "$SECRETS_FILE" <<'NODE'
process.stdout.write(JSON.stringify({
  ALVYS_CLIENT_ID: process.env.ALVYS_CLIENT_ID,
  ALVYS_CLIENT_SECRET: process.env.ALVYS_CLIENT_SECRET,
}));
NODE
  SECRETS_ARGS=(--secrets-file "$SECRETS_FILE")
fi

cd "$CF"
if [ "$VALIDATE_ONLY" = "true" ]; then
  npx wrangler deploy --dry-run --outdir "${RUNNER_TEMP:-/tmp}/wrangler-dry-run" \
    --config wrangler.generated.jsonc "${SECRETS_ARGS[@]}"
  echo "Cloudflare dry-run passed."
  exit 0
fi

DEPLOY_LOG="$(mktemp)"
npx wrangler deploy --config wrangler.generated.jsonc "${SECRETS_ARGS[@]}" | tee "$DEPLOY_LOG"

if [ -n "$APP_HOST" ]; then
  APP_URL="https://$APP_HOST"
else
  APP_URL="$(grep -oE 'https://[a-z0-9.-]+\.workers\.dev' "$DEPLOY_LOG" | head -1)"
fi
: "${APP_URL:?could not determine the deployed URL}"

wait_for() {
  url="$1"
  pattern="$2"
  for attempt in $(seq 1 18); do
    body="$(curl --fail --silent --show-error --retry 2 --retry-delay 2 --retry-all-errors "$url" 2>/dev/null || true)"
    if printf '%s' "$body" | grep -q "$pattern"; then
      return 0
    fi
    echo "Waiting for $url (attempt $attempt/18)..."
    sleep 10
  done
  echo "::error::Smoke test failed: $url"
  return 1
}

echo "==> Smoke testing $APP_URL"
wait_for "$APP_URL/health" "Healthy"
wait_for "$APP_URL/" "<app-root"
wait_for "$APP_URL/api/dashboard" "activeCustomers"

echo "Deployed: $APP_URL"
if [ -n "${GITHUB_OUTPUT:-}" ]; then echo "url=$APP_URL" >> "$GITHUB_OUTPUT"; fi
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then echo "Freight Ops deployed: $APP_URL" >> "$GITHUB_STEP_SUMMARY"; fi
