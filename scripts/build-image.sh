#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet restore Steervo.sln --locked-mode
dotnet publish src/Steervo.Web -c Release --no-restore --disable-build-servers -m:1 -p:UseAppHost=false -o .artifacts/publish
docker build --pull=false -t "${STEERVO_IMAGE:-steervo:local}" .
