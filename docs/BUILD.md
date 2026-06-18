# Build

## Prerequisites

- **.NET 8 SDK** — `dotnet --version` should report `8.0.x`.
- **Docker** (for Postgres / Redis / MinIO during development).
- **Dalamud development environment** (for the plugin half): a working FFXIV install with [XIVLauncher + Dalamud](https://goatcorp.github.io/) and the dev plugins folder accessible. The plugin csproj resolves Dalamud's assemblies from `%AppData%\XIVLauncher\addon\Hooks\dev\` — override with `-p:DalamudLibPath=…` if yours is elsewhere.

## One-time setup

```bash
# 1. Restore everything
dotnet restore Aethernet.sln

# 2. Tools (just dotnet-ef for migrations)
dotnet tool install -g dotnet-ef --version 8.*

# 3. Bring up infra
cd deploy && docker compose up -d
```

Create a strong JWT signing key and put it in user secrets (or environment variables) for each service:

```bash
dotnet user-secrets --project src/Aethernet.AuthService set Jwt:SigningKey "$(openssl rand -base64 48)"
dotnet user-secrets --project src/Aethernet.Server      set Jwt:SigningKey "$(openssl rand -base64 48)"
dotnet user-secrets --project src/Aethernet.FileServer  set Jwt:SigningKey "$(openssl rand -base64 48)"
# All three services must share the SAME signing key (one issuer, three resource servers).
```

## Migrations

```bash
dotnet ef migrations add Initial -p src/Aethernet.Data -s src/Aethernet.Server
dotnet ef database update         -p src/Aethernet.Data -s src/Aethernet.Server
```

## Run the services

In three terminals:

```bash
dotnet run --project src/Aethernet.AuthService  # http://localhost:5001
dotnet run --project src/Aethernet.Server       # http://localhost:5002
dotnet run --project src/Aethernet.FileServer   # http://localhost:5003
```

(Ports come from `Properties/launchSettings.json` per project — not committed in this scaffold; add a `launchSettings.json` per service or set `ASPNETCORE_URLS=http://+:5001` etc.)

## Build the plugin

```bash
dotnet build src/Aethernet.Plugin -c Release
```

The output is `src/Aethernet.Plugin/bin/Release/Aethernet.Plugin.dll` plus `aethernet.json`. Drop that folder into Dalamud's "dev plugins" path (typically `%AppData%\XIVLauncher\devPlugins\Aethernet`) and load `/xlplugins → installed → Aethernet`.

## Smoke test

1. Hub `GET /api/info` should return `{ "protocolVersion": 1, "build": "0.1.0", "motd": "Welcome to Aethernet." }`.
2. `POST /auth/register` with empty body should return `{ "UID": "u-…", "SecretKey": "k-…" }`.
3. `POST /auth/login` with that UID/secret should return a JWT.
4. Connect that JWT to the hub via `wscat` or the plugin's connect button — `Heartbeat` should return a UTC timestamp.

## Tests

There are no tests in this scaffold — adding them is on the very-short list. The shape we'll grow into:

- `tests/Aethernet.Server.IntegrationTests/` — `WebApplicationFactory<Program>` driving the hub end-to-end against an in-memory Postgres (Testcontainers).
- `tests/Aethernet.Plugin.UnitTests/` — pure-logic tests for `PairResolver` permission math, `CharacterDataCollector` filtering, etc.
- `tests/Aethernet.FileServer.IntegrationTests/` — round-trip a real blob through upload → has → download with the in-memory disk backend.
