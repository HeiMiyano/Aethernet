# Architecture

Aethernet has four runtime components plus a couple of shared libraries.

```
                ┌─────────────────────────┐
                │   Aethernet.Plugin      │      (Dalamud, runs in FFXIV)
                │  • IPC bridges          │
                │  • Hub client (SignalR) │
                │  • File client (HTTPS)  │
                │  • UI (ImGui)           │
                └────────┬──┬─────────────┘
            JWT login    │  │  blob up/down (HTTPS)
                         │  │
            ┌────────────▼──┴──────────────┐
            │      Aethernet.AuthService    │   ← /auth/register, /auth/login,
            │   (REST, JWT issuer)           │     Discord OAuth, refresh tokens
            └────────────┬───────────────────┘
                         │
                ┌────────▼────────────────────┐
                │   Aethernet.Server (Hub)    │   ← SignalR (MessagePack)
                │   • presence + fan-out      │     /aethernet hub mount
                │   • pairing & groups        │
                └─────────┬─────────┬─────────┘
                          │         │
                 ┌────────▼──┐   ┌──▼──────────┐
                 │ PostgreSQL │   │   Redis     │  (presence + signalr backplane)
                 │ (Npgsql)   │   └─────────────┘
                 └────────────┘

                ┌─────────────────────────────┐
                │   Aethernet.FileServer       │  ← REST, content-addressed
                │   • /files/has               │     blob CDN (SHA-1)
                │   • /files/upload (multipart)│
                │   • /files/{hash} (Range)    │
                │   • storage: S3 / disk       │
                └────────────┬─────────────────┘
                             │
                       ┌─────▼──────┐
                       │ MinIO / S3  │
                       └─────────────┘
```

## Data flow: pushing your appearance to a paired player

1. **Local change** — Penumbra, Glamourer, or another supported plugin announces a change (or a paired player just came online).
2. **`SyncOrchestrator`** debounces and calls **`CharacterDataCollector.CollectAsync()`** on the framework thread. This calls each IPC bridge, hashes any Penumbra file replacements into the local file cache, and builds a `CharacterDataDto`.
3. **`FileTransferService.UploadMissingAsync()`** asks the file server which hashes it does not yet have (`POST /files/has`), then uploads the missing blobs via `POST /files/upload` (multipart with the hash in the form).
4. **`HubConnectionService.InvokeAsync(UserPushData, …)`** sends the small dto (hashes + state strings) over SignalR to the hub.
5. The hub's **`AethernetHub.UserPushData`** filters the recipient list to active pairs and hands off to **`CharacterDataDispatcher.DispatchAsync()`**, which calls `UserReceiveCharacterData` on each recipient's connection.

## Data flow: receiving and applying

1. **`HubConnectionService`** delivers `OnlineUserCharaDataMessageDto` to **`CharacterDataApplier.HandleAsync()`**.
2. The applier filters the dto through `UserPermissions` (so e.g. animations are stripped if the local user opted out).
3. **`FileTransferService.DownloadManyAsync()`** fetches any blob hashes not in the local cache (parallel, bounded by `MaxParallelDownloads`).
4. On the framework thread, the applier creates a Penumbra temporary collection per UID, assigns it to the visible object, calls `AddTemporaryMod`, applies Glamourer state, Customize+, Honorific, Heels, Moodles, Pet Names, and finally `RedrawObject`.
5. When the player leaves visibility, `VisibleUserManager` fires `PlayerBecameInvisible`, and the applier deletes the temporary collection so other users aren't carrying around state for someone they can no longer see.

## Threading model (plugin)

- **Framework thread (Dalamud):** every call that touches the game world or another plugin's IPC (`Penumbra*`, `Glamourer*`, etc.) must run here. `CharacterDataCollector` and `CharacterDataApplier` both hop on with `IFramework.RunOnTick(...)`.
- **Background tasks:** networking (SignalR, HttpClient), hashing, disk I/O, debouncing.
- **UI thread:** ImGui callbacks via Dalamud's draw delegate. UI code reads from `PairManager`/`GroupManager` snapshots; it never blocks on network.

## Threading model (servers)

- ASP.NET Core handles request concurrency. SignalR hub instances are scoped per invocation.
- Presence tracking is in **`RedisPresenceTracker`**, which stores `uid -> hash<connectionId, machineName>` in Redis. With the Redis backplane, multiple hub instances see each other's connections.
- Heavy work in the hub (file fan-out, group changes) goes through scoped services that take a `DbContext` — Postgres serializes the writes.

## Trust model

- **Auth:** the only secret a user holds is their `secret_key` (returned once at registration). It's PBKDF2-hashed at rest. Logging in returns a JWT (12 h default) and a refresh token (30 d, single-use, rotated).
- **The file server trusts hashes.** It verifies the upload bytes hash to the claimed SHA-1; mismatches return 400. This stops one user from polluting another user's hash space.
- **The hub trusts JWTs.** Every hub call resolves the caller's UID from `ClaimTypes.NameIdentifier` — no method takes a UID parameter for the *caller's* identity. Recipient UIDs are filtered through `PairResolver` so you can't push data to people who haven't paired with you.
- **Groups** rotate passwords; password verification uses the same PBKDF2 hasher.

## Where to read code first

- `src/Aethernet.API/HubMethods.cs` — the contract: every server↔client call name.
- `src/Aethernet.Server/Hubs/AethernetHub.cs` — the bulk of the server-side surface.
- `src/Aethernet.Plugin/Services/HubConnectionService.cs` — the client-side mirror; you can read these two files side by side.
- `src/Aethernet.Plugin/Services/CharacterDataCollector.cs` and `CharacterDataApplier.cs` — the heart of the sync logic.
