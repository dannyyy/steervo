#!/usr/bin/env bash
# Formats all source files with CSharpier and Prettier.
# With --check nothing is written; the exit code reports whether files deviate.
set -euo pipefail
cd "$(dirname "$0")/.."

mode="${1:-}"
if [[ "$mode" != "" && "$mode" != "--check" ]]; then
  echo 'Usage: scripts/format.sh [--check]' >&2
  exit 1
fi

dotnet tool restore > /dev/null

if [[ "$mode" == "--check" ]]; then
  dotnet csharpier check .
  npx --no-install prettier --check .
else
  dotnet csharpier format .
  npx --no-install prettier --write .
fi
