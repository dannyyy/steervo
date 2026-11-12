#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ -e .env ]]; then
  echo '.env already exists; leaving it unchanged.'
  exit 0
fi
umask 077
database_password=$(openssl rand -hex 24)
setup_token=$(openssl rand -hex 32)
cat > .env <<EOF
POSTGRES_PASSWORD=$database_password
Setup__Token=$setup_token
ConnectionStrings__Database='Host=localhost;Port=55432;Database=steervo;Username=steervo;Password=$database_password'
PublicUrl=http://localhost:5180
AllowedHosts='*'
Smtp__Host=localhost
Smtp__Port=51025
Smtp__StartTls=false
Smtp__From=steervo@example.test
Smtp__HourlyLimit=50
Smtp__DailyLimit=200
ASPNETCORE_ENVIRONMENT=Development
DataProtection__Path=.data/keys
DocumentStorage__Provider=FileSystem
DocumentStorage__Path=.data/documents
EOF
echo 'Created .env. The single-use installation key is stored in Setup__Token.'
