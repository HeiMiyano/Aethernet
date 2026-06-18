# Running the Aethernet server side

End-to-end recipe for bringing the three services up on a developer workstation. Production deployment lives in [`DEPLOY.md`](DEPLOY.md).

## 0. One-time prerequisites

- **.NET 8 SDK** — `dotnet --version` should print `8.0.x`.
- **Docker Desktop** (or Podman, or a real Linux Docker daemon).
- **`dotnet-ef` global tool** — `dotnet tool install -g dotnet-ef --version 8.*`.
- A shell. The examples use bash / zsh; PowerShell users can usually just translate `$(openssl …)` to a fixed string.

Clone the repo and `cd` into it. All paths in this doc are relative to the repo root (`Aethernet Sync/`).

## 1. Bring up the infrastructure containers

```bash
cd deploy
docker compose up -d
```

This starts three containers:

| Container | Port      | Purpose                                              |
|-----------|-----------|------------------------------------------------------|
| `postgres`| `5432`    | Aethernet's only database.                            |
| `redis`   | `6379`    | SignalR backplane + presence tracker.                 |
| `minio`   | `9000/9001` | S3-compatible blob store for the file server.       |

Verify:

```bash
docker compose ps           # all should be Up (healthy) within ~10s
docker compose logs -f      # ^C when you've seen "database system is ready to accept connections"
```

Create the MinIO bucket that the file server expects (one-time):

```bash
docker run --rm --network host \
  -e MC_HOST_local=http://aethernet:aethernet_dev_password@localhost:9000 \
  minio/mc mb local/aethernet-blobs
```

(Or open <http://localhost:9001>, log in with `aethernet` / `aethernet_dev_password`, and create the bucket from the UI.)

## 2. Generate a JWT signing key (shared by all three services)

```bash
JWT_KEY="$(openssl rand -base64 48)"
echo "JWT_KEY=$JWT_KEY"
```

Stash it in .NET user secrets for each service:

```bash
dotnet user-secrets --project src/Aethernet.AuthService set Jwt:SigningKey "$JWT_KEY"
dotnet user-secrets --project src/Aethernet.Server      set Jwt:SigningKey "$JWT_KEY"
dotnet user-secrets --project src/Aethernet.FileServer  set Jwt:SigningKey "$JWT_KEY"
```

> The auth service issues JWTs; the hub and file server validate them. All three must share the same key.

## 3. Generate the initial EF migration and apply it

```bash
dotnet ef migrations add Initial \
  --project src/Aethernet.Data \
  --startup-project src/Aethernet.Server

dotnet ef database update \
  --project src/Aethernet.Data \
  --startup-project src/Aethernet.Server
```

You should see Postgres get the `users`, `pairs`, `groups`, `group_pairs`, `group_bans`, `file_cache`, `refresh_tokens`, `profile_reports`, `audit_log`, `banned_users`, and `blocks` tables.

Spot-check:

```bash
docker exec -it deploy-postgres-1 psql -U aethernet -d aethernet -c '\dt'
```

## 4. Boot the three .NET services

Open three terminals (or use `tmux` / your IDE's task runner).

**Terminal 1 — Auth service:**

```bash
ASPNETCORE_URLS=http://localhost:5001 \
  dotnet run --project src/Aethernet.AuthService
```

**Terminal 2 — SignalR hub:**

```bash
ASPNETCORE_URLS=http://localhost:5002 \
  dotnet run --project src/Aethernet.Server
```

**Terminal 3 — File server:**

```bash
ASPNETCORE_URLS=http://localhost:5003 \
  dotnet run --project src/Aethernet.FileServer
```

You should see Serilog spew for each, ending with a "Now listening on" line.

## 5. Smoke-test the server side

These are pure HTTP calls — copy/paste into a terminal. No FFXIV needed.

### a) Server info

```bash
curl -s http://localhost:5002/api/info | jq
```
Expected:
```json
{ "protocolVersion": 1, "build": "0.1.0", "motd": "Welcome to Aethernet." }
```

### b) Health checks

```bash
curl -s http://localhost:5001/healthz
curl -s http://localhost:5002/healthz
curl -s http://localhost:5003/healthz
```
Each should return `Healthy` (200).

### c) Register a test account

```bash
REG=$(curl -s -X POST http://localhost:5001/auth/register \
       -H 'Content-Type: application/json' -d '{}')
echo $REG | jq
export UID=$(echo $REG | jq -r .UID)
export KEY=$(echo $REG | jq -r .SecretKey)
echo "UID=$UID  KEY=$KEY"
```

### d) Log in (get a JWT)

```bash
LOGIN=$(curl -s -X POST http://localhost:5001/auth/login \
          -H 'Content-Type: application/json' \
          -d "{\"UID\":\"$UID\",\"SecretKey\":\"$KEY\"}")
echo $LOGIN | jq
export JWT=$(echo $LOGIN | jq -r .AccessToken)
```

### e) Upload a tiny blob to the file server

```bash
echo "hello aethernet" > /tmp/blob.txt
HASH=$(sha1sum /tmp/blob.txt | cut -d' ' -f1 | tr a-z A-Z)
echo "HASH=$HASH"

curl -s -X POST http://localhost:5003/files/upload \
  -H "Authorization: Bearer $JWT" \
  -F "hash=$HASH" -F "file=@/tmp/blob.txt" | jq
```
Expected: `{ "Hash": "…", "Size": 16, "AlreadyExisted": false }`. Re-running returns `AlreadyExisted: true`.

### f) Round-trip the blob

```bash
curl -s -H "Authorization: Bearer $JWT" http://localhost:5003/files/$HASH
```
Should print `hello aethernet`.

### g) Connect to the hub from the command line

Install `dotnet-signalr` (or use the plugin in step 7). The quickest path is a tiny C# snippet:

```bash
cat > /tmp/hubtest.csx <<'EOF'
#r "nuget: Microsoft.AspNetCore.SignalR.Client, 8.0.10"
#r "nuget: Microsoft.AspNetCore.SignalR.Protocols.MessagePack, 8.0.10"
using Microsoft.AspNetCore.SignalR.Client;
var jwt = Environment.GetEnvironmentVariable("JWT")!;
var conn = new HubConnectionBuilder()
    .WithUrl("http://localhost:5002/aethernet?proto=1",
        o => o.AccessTokenProvider = () => Task.FromResult<string?>(jwt))
    .AddMessagePackProtocol()
    .Build();
await conn.StartAsync();
Console.WriteLine("connected, calling Heartbeat…");
Console.WriteLine(await conn.InvokeAsync<DateTime>("Heartbeat"));
await conn.DisposeAsync();
EOF
dotnet script /tmp/hubtest.csx
```
You should see a UTC timestamp.

## 6. Build the Dalamud plugin

```bash
dotnet build src/Aethernet.Plugin -c Release
```

Output: `src/Aethernet.Plugin/bin/Release/Aethernet.Plugin.dll` + `aethernet.json`. See [`TESTING.md`](TESTING.md) for installing into Dalamud and walking through every client surface.

## 7. Day-to-day workflow

- **Iterating on a service:** kill its terminal, `dotnet run` again. Postgres and Redis keep their state.
- **Resetting the database:** `docker compose down -v && docker compose up -d`, then re-apply migrations.
- **Watching logs:** Serilog writes to stdout. For prod-style file logs, swap in `WriteTo.File(...)`.
- **Updating migrations:** edit an entity, then `dotnet ef migrations add <Name>` followed by `dotnet ef database update`.

## 8. Promoting an account to moderator/admin

There is no admin UI yet. Until there is, twiddle the column directly:

```bash
docker exec -it deploy-postgres-1 psql -U aethernet -d aethernet \
  -c "UPDATE users SET \"IsAdmin\"=true WHERE \"Uid\"='u-...';"
```

After promotion, the user's JWT picks up the role on the next login. They can then call `ModerationBanUser`, `ModerationGetReports`, and the `/admin/files/*` endpoints.

## 9. Tear down

```bash
cd deploy && docker compose down              # keeps data
cd deploy && docker compose down -v           # nukes the volumes too
```

## Troubleshooting

- **"Connection refused" on hub:** check `ASPNETCORE_URLS` actually bound. `lsof -i :5002` should show `dotnet` LISTEN.
- **`401 Unauthorized` from the file server:** JWT signing key mismatch. Re-run the `dotnet user-secrets` block in step 2 — all three services must share the *same* `Jwt:SigningKey`.
- **`ConnectionRefused` from MinIO:** the bucket wasn't created. Re-run the `mc mb` step.
- **Migrations fail with "no such file":** you're in the wrong directory — `--project src/Aethernet.Data` is relative to the repo root.
- **Hub closes the connection immediately with a "Protocol version mismatch" message:** the client must pass `?proto=1` in the websocket URL. The plugin does this automatically; ad-hoc clients (like the `dotnet-script` snippet above) must too.
