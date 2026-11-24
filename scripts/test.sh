#!/usr/bin/env bash
# Runs the unit and integration tests against the test services; with --browser also the end-to-end tests.
set -euo pipefail
cd "$(dirname "$0")/.."

mode="${1:-}"

compose() {
  docker compose -f compose.test.yaml "$@"
}

# Runs one SQL statement in the PostgreSQL test container.
run_sql() {
  compose exec -T postgres18 psql -U steervo -d steervo -c "$1"
}

drop_browser_database() {
  run_sql "DROP DATABASE $browser_database WITH (FORCE)" > /dev/null
}

# Fail before the long test run when the browser tests cannot start anyway.
if [[ "$mode" == "--browser" ]]; then
  if ! node --input-type=module -e "await import('playwright')" > /dev/null 2>&1; then
    echo 'Browser tests require existing Node.js and Playwright dependencies; see README.md.' >&2
    exit 1
  fi
fi

compose up -d --wait
# Formatting is part of the checks; see docs/code-style.md.
scripts/format.sh --check

dotnet restore Steervo.sln --locked-mode
dotnet test Steervo.sln --no-restore
dotnet build src/Steervo.Web --no-restore

if [[ "$mode" == "--browser" ]]; then
  # Every run gets its own database, which is removed again when the script exits.
  browser_database="steervo_browser_$(date +%s)_$RANDOM"
  run_sql "CREATE DATABASE $browser_database"
  trap drop_browser_database EXIT

  export STEERVO_E2E_DATABASE="Host=localhost;Port=55438;Database=$browser_database;Username=steervo;Password=steervo-local-only"
  export STEERVO_E2E_DATABASE_CONTAINER
  STEERVO_E2E_DATABASE_CONTAINER=$(compose ps -q postgres18)

  npm run test:e2e
fi
