# Aethernet

A Dalamud plugin and server stack that synchronizes Penumbra mods, Glamourer settings, and related character-customization plugins between paired FFXIV players.

> **Status: in active development.** End-to-end mod sync works between paired users; the codebase is shipping iteratively.

---

## What it does

When two players have Aethernet installed and pair with each other, the plugin:

1. Detects when a paired player is visible nearby in-game.
2. Collects the local player's currently-applied Penumbra mod files, Glamourer state, Customize+ profile, Honorific title, SimpleHeels offset, Moodles status, and Pet Names.
3. Hashes each mod file (SHA-1) and uploads any blobs the server doesn't already have to the content-addressed file server.
4. Pushes a compact "character data" envelope (hashes + manipulation strings + small state blobs) through the SignalR hub to each paired visible player.
5. The receiving client downloads any missing file blobs by hash, redirects them into Penumbra via a temporary collection, applies the Glamourer / Customize+ / Honorific / Heels / Moodles state, and the paired player sees the modded character.

Groups ("syncshells") let many players share a single rotating password and become pairs en-masse.

## Components

| Project | Type | Purpose |
|---|---|---|
| `Aethernet.Plugin` | Dalamud plugin (net10.0-windows) | The client — runs in FFXIV, hooks Penumbra/Glamourer IPC, talks to the server. |
| `Aethernet.Server` | ASP.NET Core | SignalR hub: pairing, presence, character-data push, groups, moderation. |
| `Aethernet.AuthService` | ASP.NET Core | Registration (Discord OAuth + secret-key), JWT issuance, refresh, recovery. |
| `Aethernet.FileServer` | ASP.NET Core | Content-addressed upload/download of mod blobs by SHA-1. |
| `Aethernet.API` | netstandard2.0 library | DTOs + hub method constants shared by client and server. |
| `Aethernet.Shared` | net8.0 library | Cross-cutting utilities (hashing, logging, JSON config). |
| `Aethernet.Data` | net8.0 library | EF Core `DbContext`, entities, migrations (Npgsql / PostgreSQL). |

## Quick start (dev)

Prerequisites: .NET 10 SDK, Docker, Dalamud dev environment for the plugin half.

```bash
# 1. Bring up Postgres + Redis + MinIO (for file blobs)
cd deploy && docker compose up -d

# 2. Apply migrations
dotnet ef database update -p src/Aethernet.Data -s src/Aethernet.Server

# 3. Run the three services
dotnet run --project src/Aethernet.AuthService
dotnet run --project src/Aethernet.Server
dotnet run --project src/Aethernet.FileServer

# 4. Build the plugin and load it into Dalamud dev plugins
dotnet build src/Aethernet.Plugin -c Release
```

See [`docs/BUILD.md`](docs/BUILD.md) and [`docs/DEPLOY.md`](docs/DEPLOY.md) for the full story.

## Documentation

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — component diagram, data flow, threading model.
- [`docs/PROTOCOL.md`](docs/PROTOCOL.md) — SignalR hub methods, DTO shapes, file-server REST API.
- [`docs/FEATURE_PARITY.md`](docs/FEATURE_PARITY.md) — Aethernet feature checklist with current status.
- [`docs/BUILD.md`](docs/BUILD.md) — local build instructions for each component.
- [`docs/DEPLOY.md`](docs/DEPLOY.md) — production deployment guide.

## License

To be decided.

## Disclaimer

Aethernet is unofficial, not affiliated with or endorsed by Square Enix or any other project. Mod synchronization is a sensitive area; use responsibly.
