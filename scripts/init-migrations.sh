#!/usr/bin/env bash
# Generates the Initial EF migration and applies it. Run once after first clone.
set -euo pipefail
cd "$(dirname "$0")/.."

if ! command -v dotnet >/dev/null; then
  echo "dotnet SDK 8 is required (https://dotnet.microsoft.com/download)"; exit 1
fi
if ! dotnet ef --version >/dev/null 2>&1; then
  echo "Installing dotnet-ef…"
  dotnet tool install -g dotnet-ef --version 8.* || true
  export PATH="$PATH:$HOME/.dotnet/tools"
fi

dotnet ef migrations add Initial \
  --project src/Aethernet.Data \
  --startup-project src/Aethernet.Server
dotnet ef database update \
  --project src/Aethernet.Data \
  --startup-project src/Aethernet.Server

echo
echo "Done. Schema applied to the connection string in src/Aethernet.Server/appsettings.json"
echo "Override with the AETHERNET_DB environment variable for a different target."
