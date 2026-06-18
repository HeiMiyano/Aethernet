# Deploy

A reference production topology and the knobs you'll want to turn before exposing Aethernet to real users.

## Topology

```
              ┌────────────┐
              │  Cloudflare │  TLS termination, optional caching for GET /files/{hash}
              └─────┬───────┘
                    │
        ┌───────────┼───────────────┐
        │           │               │
   auth.aethernet  hub.aethernet  files.aethernet
        │           │               │
    ┌───▼───┐   ┌───▼───┐       ┌───▼───┐
    │ Auth  │   │ Hub × N│       │ Files │
    └───┬───┘   └────┬───┘       └───┬───┘
        │            │               │
        └────┬───────┴───────┬───────┘
             ▼               ▼
        ┌────────┐      ┌─────────┐
        │Postgres│      │  S3/R2  │
        └────────┘      └─────────┘
                   ┌────────┐
                   │ Redis  │ (signalr backplane + presence)
                   └────────┘
```

- **Auth** can run as a single instance behind autoscaling; nothing it does is per-instance state.
- **Hub** scales out horizontally — the Redis backplane fans messages between instances. Use a sticky-session aware load balancer or websocket-aware ingress (Cloudflare Tunnels, Traefik, nginx with proxy_pass).
- **Files** can run as many instances as you like; they're stateless except for the Postgres metadata. Heavy users push the cost into bandwidth + storage, not CPU.
- **Postgres** — managed instance with PITR. Aethernet's schema is small (<1 GB even with millions of users); the heavy hitter is `FileCache`, which still only stores metadata.
- **Redis** — managed instance, persistence optional but recommended.
- **Object storage** — S3, Cloudflare R2, Backblaze B2, or MinIO on bare metal. Hashes are uppercase SHA-1; layout is `{first2}/{next2}/{full}` if you switch from S3 to a disk backend.

## Secrets

The three services share a JWT signing key (auth issues, hub + files verify). Rotate it like this:

1. Generate a new key.
2. Roll it out to all three services as a *secondary* validator (modify `TokenValidationParameters` to accept either key).
3. Wait one access-token lifetime (default 12 h).
4. Make the new key the *primary* in auth.
5. Wait another 12 h.
6. Remove the old key from hub + files.

For per-environment secrets:

- Use the platform's secret manager (AWS Secrets Manager, GCP Secret Manager, Doppler, Vault, etc.) and inject as env vars `Jwt__SigningKey`, `ConnectionStrings__Default`, `Storage__SecretKey`, `Discord__ClientSecret`.
- Never commit `appsettings.Production.json` with real secrets — the `.gitignore` already excludes `appsettings.Development.json`.

## Migrations in CI

Add this step to your deploy pipeline before rolling out new pods:

```bash
dotnet ef database update --project src/Aethernet.Data --startup-project src/Aethernet.Server
```

EF migrations are forward-only and locked with a Postgres advisory lock, so multiple concurrent deploys are safe.

## TLS / certificates

The plugin ships configured for HTTPS endpoints. Don't expose any of the three services over plain HTTP in production — JWTs are bearer tokens; capture = impersonation.

## Capacity planning rules of thumb

- One user with full Penumbra makeup uploads ~50–500 MB of unique blobs the first time and ~0 thereafter (dedup catches the rest). Provision storage as ~200 MB × active users × 2 (one for current state, one for in-flight history).
- One paired pair generates ~one push per minute on average (zone changes, gear swaps); a 50-pair user generates ~50 pushes/min. The hub forwards each push to N recipients, so the fan-out at the hub scales as `pushes × recipients`. Sizing target: 1 vCPU per 5k concurrent connections.
- Redis is mostly idle; one small managed instance suffices for hundreds of thousands of connections.

## Health & observability

- All services expose `/healthz` (liveness) and a DbContext check.
- Hub exposes `/metrics` (prometheus-net). Add scrapes from your monitoring stack.
- Recommended dashboards: hub connections, push rate / recipient, p99 push latency, file upload bytes/sec, blob dedup ratio, Postgres connection pool saturation.

## Moderation

Aethernet ships with three moderation surfaces; build a UI/CLI around them when you go public:

1. `BannedUserEntity` — `UserGetOnlinePairs` and the hub's `OnConnectedAsync` already reject banned UIDs.
2. `FileCacheEntity.IsForbidden` — when set, `/files/{hash}` returns 451 and `HasFilesResponse.Forbidden` lists the hash.
3. `ProfileReportEntity` — populated by `UserReportProfile`; build a queue/dashboard for moderators.

## Disclaimer

Aethernet is unofficial. Mod synchronization can put strain on game servers; rate-limit aggressively. Coordinate with the FFXIV Penumbra/Glamourer community before announcing a public server.
