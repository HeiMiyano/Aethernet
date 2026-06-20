# Aethernet Cloud Deployment Runbook

Migrating from the Hyper-V VM + Cloudflare Tunnel setup (`docker-compose.hyperv.yml`)
to a cloud-native Hetzner Cloud VPS + Cloudflare R2 deployment (`docker-compose.cloud.yml`).

**Cost expectations** (recap from architectural discussion):

| Scale | Hetzner VPS | R2 storage | R2 ops | Egress | **Total/mo** |
|-------|------------|-----------|--------|--------|-------------|
| 10 users | CPX21 ($8) | 30 GB ($0.30) | free | free | **~$8** |
| 100 users | CPX31 ($15) | 300 GB ($4.50) | free | free | **~$20** |
| 500 users | CPX41 ($30) | 1.5 TB ($22.50) | free | free | **~$55** |

---

## Phase 0 — Decide a maintenance window

Plan for ~2-3 hours total. Most steps are non-disruptive, but the **DNS cutover** (Phase 5) will
briefly bounce active client connections. Pick a low-traffic time and tell paired users to expect
a brief reconnect.

Existing data preserved through the migration:
- ✅ User accounts, UIDs, secret keys (Postgres dump → restore)
- ✅ Pair relationships (Postgres)
- ✅ All blob files (rclone copy → R2)
- ❌ Redis presence (ephemeral; just reconnect)

---

## Phase 1 — Cloudflare R2 setup (~15 min)

1. **Sign up for R2** at <https://dash.cloudflare.com/> → R2 in the left nav. Card required;
   the free tier (10 GB storage, 1M Class A, 10M Class B ops/month) is enough for ~10 users.

2. **Create the bucket**:
   - R2 → "Create bucket"
   - Name: `aethernet-blobs`
   - Location: `Automatic` (Cloudflare picks the closest data center)
   - Default storage class: `Standard`
   - Click Create.

3. **Create an API token**:
   - R2 → "Manage R2 API Tokens" → "Create API token"
   - Token name: `aethernet-files-server`
   - Permissions: **Object Read & Write**
   - Specify bucket: `aethernet-blobs` (scoped — least privilege)
   - TTL: leave as default
   - Click Create and **save the Access Key ID + Secret Access Key immediately** — the secret
     is shown once.

4. **Find your account ID**: R2 → Overview → right sidebar shows the S3 API endpoint:
   `https://<account-id>.r2.cloudflarestorage.com`. Note this URL.

5. **Optional: enable Cloudflare cache in front of R2** for popular blobs (set up later, after
   you have public access configured). Lets repeated downloads of the same hash serve from edge.

---

## Phase 2 — Provision the Hetzner VPS (~30 min)

1. **Sign up** at <https://www.hetzner.com/cloud> → create a project named `aethernet`.

2. **Generate an SSH key** locally if you don't have one:
   ```powershell
   ssh-keygen -t ed25519 -C "aethernet-deploy"
   # Default location: ~/.ssh/id_ed25519
   ```
   Add the public key (`~/.ssh/id_ed25519.pub`) under Project → Security → SSH Keys.

3. **Create the server**:
   - Location: pick closest to your majority of users (Ashburn VA for US, Falkenstein for EU)
   - Image: `Ubuntu 24.04` (or latest LTS)
   - Type: **CPX21** for now (~$8/mo, 3 vCPU/4 GB/80 GB)
   - SSH key: select the one you uploaded
   - Name: `aethernet-prod-1`
   - Firewall: skip for now — we'll add one in step 4
   - Create & Buy now

4. **Add a firewall** (Project → Firewalls → Create):
   - Inbound: TCP 22 (SSH from your IP only), TCP 80 + 443 (HTTP/HTTPS from anywhere)
   - Outbound: allow all
   - Apply to `aethernet-prod-1`

5. **First-time setup** (SSH in as `root`):
   ```bash
   ssh root@<vps-ip>

   # Create the non-root user we'll deploy as
   adduser aethernet
   usermod -aG sudo aethernet
   # Copy your SSH key over so you can ssh in as aethernet directly
   mkdir -p /home/aethernet/.ssh
   cp /root/.ssh/authorized_keys /home/aethernet/.ssh/
   chown -R aethernet:aethernet /home/aethernet/.ssh
   chmod 700 /home/aethernet/.ssh
   chmod 600 /home/aethernet/.ssh/authorized_keys

   # Lock down root SSH
   sed -i 's/^#\?PermitRootLogin.*/PermitRootLogin no/' /etc/ssh/sshd_config
   systemctl restart ssh
   exit
   ```

6. **Install Docker + Docker Compose** (as `aethernet`):
   ```bash
   ssh aethernet@<vps-ip>

   # Docker official install script
   curl -fsSL https://get.docker.com | sudo sh
   sudo usermod -aG docker aethernet
   # Re-login so the group change takes effect
   exit
   ssh aethernet@<vps-ip>

   # Verify
   docker --version
   docker compose version
   ```

7. **Clone the Aethernet repo**:
   ```bash
   cd ~
   git clone https://github.com/HeiMiyano/Aethernet.git aethernet
   cd aethernet
   ```

---

## Phase 3 — Migrate existing blobs (~30-60 min)

Copies your current ~17 GB of blob storage from the Hyper-V VM to R2.

1. **On the Hyper-V VM**, install rclone:
   ```bash
   sudo apt install rclone
   ```

2. **Configure an R2 remote**:
   ```bash
   rclone config
   ```
   Walk through:
   - `n` (new remote) → name: `r2`
   - Storage: `4` (Amazon S3 Compliant)
   - provider: select `Cloudflare`
   - env_auth: `1` (enter credentials)
   - access_key_id: paste your R2 Access Key
   - secret_access_key: paste your R2 Secret Key
   - region: `auto`
   - endpoint: `https://<account-id>.r2.cloudflarestorage.com`
   - Accept defaults for the rest.

3. **Copy blobs to R2**:
   ```bash
   # Find your blob volume mount on the VM
   docker volume inspect aethernet-hyperv_blob-storage --format '{{ .Mountpoint }}'
   # Probably: /var/lib/docker/volumes/aethernet-hyperv_blob-storage/_data

   # Dry-run first
   sudo rclone copy --dry-run \
     /var/lib/docker/volumes/aethernet-hyperv_blob-storage/_data \
     r2:aethernet-blobs \
     --progress --transfers 8 --checkers 16

   # Real run (drop --dry-run)
   sudo rclone copy \
     /var/lib/docker/volumes/aethernet-hyperv_blob-storage/_data \
     r2:aethernet-blobs \
     --progress --transfers 8 --checkers 16
   ```

   For ~17 GB on a residential connection this takes 10-30 min depending on upstream bandwidth.

4. **Verify**:
   ```bash
   rclone size r2:aethernet-blobs
   # Should match the source dir's size
   ```

---

## Phase 4 — Migrate the Postgres database (~10 min)

1. **On the Hyper-V VM**, dump the database:
   ```bash
   cd ~/aethernet
   docker compose -f deploy/docker-compose.hyperv.yml exec -T postgres \
     pg_dump -U aethernet aethernet > /tmp/aethernet-pgdump.sql
   ls -lh /tmp/aethernet-pgdump.sql
   # Should be tens of KB to a few MB
   ```

2. **Copy the dump to the cloud VPS**:
   ```bash
   scp /tmp/aethernet-pgdump.sql aethernet@<vps-ip>:/tmp/
   ```

3. **Configure secrets on the VPS** (before restoring — Postgres needs to be up first):
   ```bash
   ssh aethernet@<vps-ip>
   cd ~/aethernet/deploy
   cp .env.cloud.example .env
   nano .env  # fill in all REPLACE_ME values, especially:
              # - POSTGRES_PASSWORD: copy from Hyper-V VM's .env (so the dump works)
              # - JWT_SIGNING_KEY: copy from Hyper-V VM's .env (so existing JWTs still valid)
              # - R2_ENDPOINT, R2_ACCESS_KEY, R2_SECRET_KEY: from Phase 1
   ```

   **Important**: reuse the existing `POSTGRES_PASSWORD` and `JWT_SIGNING_KEY` from the
   Hyper-V VM. Otherwise users will be forced to re-register.

4. **Bring up Postgres only** so we can restore into it:
   ```bash
   docker compose -f docker-compose.cloud.yml up -d postgres
   sleep 10  # let postgres finish initializing
   ```

5. **Restore the dump**:
   ```bash
   # The schema gets re-created from the dump; drop any auto-created tables first
   docker compose -f docker-compose.cloud.yml exec -T postgres \
     psql -U aethernet -d aethernet -c "DROP SCHEMA public CASCADE; CREATE SCHEMA public;"

   cat /tmp/aethernet-pgdump.sql | \
     docker compose -f docker-compose.cloud.yml exec -T postgres \
     psql -U aethernet -d aethernet
   ```

6. **Quick sanity check**:
   ```bash
   docker compose -f docker-compose.cloud.yml exec postgres \
     psql -U aethernet -d aethernet -c \
     "SELECT COUNT(*) AS users, (SELECT COUNT(*) FROM \"Pairs\") AS pairs FROM \"Users\";"
   ```

---

## Phase 5 — Bring up the full stack + DNS cutover (~20 min)

1. **Edit `deploy/Caddyfile`** to set your real domain envs (the file uses `{$DOMAIN_AUTH}` etc.).
   Add to your `.env`:
   ```bash
   echo "DOMAIN_AUTH=auth-aethernet.heimiyano.com"   >> ~/aethernet/deploy/.env
   echo "DOMAIN_HUB=hub-aethernet.heimiyano.com"     >> ~/aethernet/deploy/.env
   echo "DOMAIN_FILES=files-aethernet.heimiyano.com" >> ~/aethernet/deploy/.env
   echo "ACME_EMAIL=your-email@example.com"          >> ~/aethernet/deploy/.env
   ```
   (Caddy needs `ACME_EMAIL` to register with Let's Encrypt.)

2. **Update the compose file to pass those into Caddy** — they're already wired as
   `{$DOMAIN_AUTH}` style refs in the Caddyfile, but Caddy needs them in its environment.
   The `docker-compose.cloud.yml` `caddy` service should include:
   ```yaml
   environment:
     DOMAIN_AUTH:  ${DOMAIN_AUTH}
     DOMAIN_HUB:   ${DOMAIN_HUB}
     DOMAIN_FILES: ${DOMAIN_FILES}
     ACME_EMAIL:   ${ACME_EMAIL}
   ```
   *(Add this block to the caddy service in `docker-compose.cloud.yml` if it's not already there.)*

3. **Start everything**:
   ```bash
   cd ~/aethernet
   docker compose -f deploy/docker-compose.cloud.yml up -d --build
   docker compose -f deploy/docker-compose.cloud.yml ps
   docker compose -f deploy/docker-compose.cloud.yml logs --tail=30
   ```

4. **DNS cutover** (Cloudflare dashboard → your zone → DNS):
   - `auth-aethernet`, `hub-aethernet`, `files-aethernet` — change the existing CNAMEs/A records
     from Cloudflare Tunnel target → A record pointing at the **VPS public IP**.
   - **Proxy status**: orange cloud ON for `files-aethernet` (CDN cache benefit), DNS-only for
     `hub-aethernet` (WebSockets work better through Cloudflare in orange-cloud mode too, but
     start with DNS-only if you hit issues).
   - TTL: 60 seconds (helps if you need to revert).

5. **Wait for propagation + Let's Encrypt cert issuance** (~2-5 min):
   ```bash
   docker compose -f deploy/docker-compose.cloud.yml logs caddy | grep -E "obtain|certificate"
   ```
   You should see "certificate obtained successfully" for all three domains.

6. **Smoke test from your client machine**:
   ```bash
   curl -sI https://auth-aethernet.heimiyano.com/health
   curl -sI https://hub-aethernet.heimiyano.com/health
   curl -sI https://files-aethernet.heimiyano.com/files/has -X POST \
     -H "Content-Type: application/json" -d '{"hashes":[]}'
   # All should return 2xx or 401 (auth required), NOT connection refused.
   ```

7. **In-game test**: open FFXIV with Aethernet enabled. The plugin should reconnect within
   30 seconds. Verify in `dalamud.log`:
   ```
   Connected to Aethernet hub as u-...
   ```

---

## Phase 6 — Decommission the Hyper-V VM (after ~1 week of healthy operation)

Let the new deployment run for a week, monitor logs, confirm no missed pushes. Then:

```bash
# On the Hyper-V VM
cd ~/aethernet
docker compose -f deploy/docker-compose.hyperv.yml down -v   # -v also deletes volumes
```

You can keep the VM around as a hot standby (just don't run the compose stack) or delete it.

---

## Operational notes

### Monitoring

```bash
# Disk usage (R2 is unlimited but you pay per GB)
docker compose -f deploy/docker-compose.cloud.yml exec postgres psql -U aethernet -d aethernet \
  -c "SELECT COUNT(*) AS blobs, pg_size_pretty(SUM(\"SizeBytes\")) AS total
      FROM \"FileCache\" WHERE NOT \"IsForbidden\";"

# Janitor activity
docker compose -f deploy/docker-compose.cloud.yml logs files | grep -E "evict|pressure"

# Top blob owners
docker compose -f deploy/docker-compose.cloud.yml exec postgres psql -U aethernet -d aethernet \
  -c "SELECT \"FirstUploaderUid\", COUNT(*) AS blobs, pg_size_pretty(SUM(\"SizeBytes\")) AS total
      FROM \"FileCache\" GROUP BY \"FirstUploaderUid\" ORDER BY SUM(\"SizeBytes\") DESC LIMIT 10;"
```

### Cost monitoring

- Cloudflare dashboard → R2 → Overview shows monthly GB-hours stored and operations consumed.
- Hetzner dashboard → Cloud → server overview shows current billing.
- Set up a Cloudflare R2 budget alert at $5/$20/$50 so you're notified before any surprise.

### Scaling up

When you outgrow CPX21:
```bash
# In the Hetzner dashboard: server → Rescale → CPX31 (no downtime; just brief reboot)
```
Storage on R2 scales infinitely; only cost grows.

When you outgrow a single VPS (1000+ active users, 50+ concurrent uploads):
1. Move Postgres to a managed provider (Neon free tier handles 500 concurrent connections)
2. Move Redis to a managed provider (Upstash free tier handles low traffic)
3. Run multiple hub instances behind a load balancer (Hetzner has one for ~$5/mo)
4. SignalR scale-out is already wired (StackExchangeRedis package in the hub csproj)

### Rolling back to Hyper-V (emergency)

DNS cutover is the reversible bit. To roll back:
1. Cloudflare DNS → revert the three A records back to your old Cloudflare Tunnel CNAMEs
2. On the Hyper-V VM: `docker compose -f deploy/docker-compose.hyperv.yml up -d`
3. Wait 1-2 min for DNS propagation
4. Client reconnects automatically

R2 data is fine; you can keep it or delete the bucket later. The Postgres dump from Phase 4
captures the state at migration time — restore it back if you need to roll back DB state too.
