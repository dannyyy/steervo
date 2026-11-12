#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ "${1:-}" == "--browser" ]]; then
  if ! node --input-type=module -e "await import('playwright')" >/dev/null 2>&1; then
    echo 'Browser tests require existing Node.js and Playwright dependencies; see README.md.' >&2
    exit 1
  fi
fi
docker compose -f compose.test.yaml up -d --wait
dotnet restore Steervo.sln --locked-mode
dotnet test Steervo.sln --no-restore
dotnet build src/Steervo.Web --no-restore
if [[ "${1:-}" == "--browser" ]]; then
  browser_database="steervo_browser_$(date +%s)_$RANDOM"
  docker compose -f compose.test.yaml exec -T postgres18 psql -U steervo -d steervo -c "CREATE DATABASE $browser_database"
  trap 'docker compose -f compose.test.yaml exec -T postgres18 psql -U steervo -d steervo -c "DROP DATABASE $browser_database WITH (FORCE)" >/dev/null' EXIT
  export STEERVO_E2E_DATABASE="Host=localhost;Port=55438;Database=$browser_database;Username=steervo;Password=steervo-local-only"
  export STEERVO_E2E_DATABASE_CONTAINER
  STEERVO_E2E_DATABASE_CONTAINER=$(docker compose -f compose.test.yaml ps -q postgres18)
  npm run test:e2e
fi
