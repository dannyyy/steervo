#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ ! -f .env ]]; then echo 'Run scripts/init-local.sh first.' >&2; exit 1; fi
set -a
source .env
set +a
export DataProtection__Path="$PWD/.data/keys"
dotnet run --project src/Steervo.Web --no-launch-profile -- --migrate
exec dotnet run --project src/Steervo.Web --no-launch-profile --urls http://localhost:5180
