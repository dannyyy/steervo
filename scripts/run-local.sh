#!/usr/bin/env bash
# Applies pending database migrations and starts the web application with the settings from .env.
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ ! -f .env ]]; then
  echo 'Run scripts/init-local.sh first.' >&2
  exit 1
fi

# Export every variable defined in .env to the application.
set -a
source .env
set +a

# The relative paths from .env must not depend on the directory the application runs in.
export DataProtection__Path="$PWD/.data/keys"
export DocumentStorage__Path="$PWD/.data/documents"

run_web=(dotnet run --project src/Steervo.Web --no-launch-profile)

"${run_web[@]}" -- --migrate
exec "${run_web[@]}" --urls http://localhost:5180
