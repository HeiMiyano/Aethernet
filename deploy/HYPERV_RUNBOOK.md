# Aethernet on Hyper-V via Cloudflare Tunnel

End-to-end recipe for running Aethernet on a free Ubuntu VM at home, exposed to the
public internet via a Cloudflare Tunnel — no router config, no port forwarding, no
Let's Encrypt, no AWS bill. Target cost: **~$0/mo** (just electricity).

Assumes:
- Your DNS for `heimiyano.com` is already on Cloudflare ✅
- Windows 10/11 Pro or Enterprise (Hyper-V is a built-in feature of those SKUs; Home edition doesn't ship it)
- Host machine has ≥4 GB of RAM to spare and ≥40 GB of free disk

## What you'll do, in order

1. Enable Hyper-V (if not already)
2. Download Ubuntu 26.04 LTS Server ISO
3. Create + install the VM
4. Initial OS setup (network, SSH, Docker)
5. Create the Cloudflare Tunnel + tunnel token
6. Configure public hostnames (the three subdomains)
7. Clone repo, fill in `.env`, `docker compose up`
8. Apply the EF migration
9. Smoke-test the public URLs
10. Point the local plugin at the prod URLs

Total time from a clean machine: ~60–90 minutes.

---

## 1. Enable Hyper-V

If Hyper-V Manager is already in your Start menu, skip this.

PowerShell as Administrator:

```powershell
Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V -All -NoRestart
Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-Management-PowerShell -All -NoRestart
Restart-Computer
```

After the reboot, search for "Hyper-V Manager" in the Start menu to confirm.

## 2. Download Ubuntu 26.04 LTS Server ISO

Get the ARM64 or x64 ISO matching your host CPU from <https://releases.ubuntu.com/26.04/>.
Almost certainly x64 unless you're on a Windows-on-ARM laptop.

Save it somewhere like `C:\ISOs\ubuntu-26.04-live-server-amd64.iso`.

## 3. Create the VM

Open **Hyper-V Manager** → right-click your hostname → **New** → **Virtual Machine**.

Wizard steps:

- **Name**: `aethernet-vm`
- **Generation**: Generation 2 (UEFI)
- **Startup memory**: 4096 MB, ✅ **Use Dynamic Memory** so the host reclaims when idle
- **Networking**: select an **External** virtual switch. If you don't have one, cancel the wizard, in Hyper-V Manager → **Virtual Switch Manager** → New → External → bind to your physical NIC → OK, then restart the wizard. An External switch puts the VM on your LAN with its own IP (the Cloudflare Tunnel doesn't actually need this; even an Internal switch works since cloudflared dials *out*, but External is simpler because it gets DHCP from your home router).
- **Virtual hard disk**: 40 GB at the default path is plenty. Mod blobs accumulate here — bump higher if your test crew syncs heavy mods.
- **Installation options**: **Install an operating system from a bootable image file** → browse to the Ubuntu ISO.
- Finish.

Before first boot, **right-click the new VM → Settings**:

- **Security** → uncheck "Enable Secure Boot" (Ubuntu's installer can deal with it on, but turning it off avoids a few rough edges).
- **Processor** → bump virtual processors from 1 to 2.
- OK.

Start the VM → connect → follow the Ubuntu installer:

- Language → English (or whatever)
- Keyboard → your layout
- Type of install → Ubuntu Server (minimized is fine)
- Network → accept defaults; you should see an IP from your home router's DHCP
- Storage → use entire disk, LVM on, no encryption (the VHDX is already on your encrypted host disk if you've turned BitLocker on)
- Profile → `aethernet` user, `aethernet-vm` hostname, set a strong password
- SSH → ✅ Install OpenSSH server, you'll want it
- Featured server snaps → skip them all
- Install → wait ~5–10 min → Reboot Now → remove ISO from DVD drive (Settings → DVD Drive → None) when prompted

Once it's back up at the login prompt, switch to your host PowerShell and SSH in (more pleasant than the Hyper-V console window):

```powershell
# Find the VM's IP from inside the VM with:  ip -4 a | grep eth0
ssh aethernet@192.168.x.x
```

## 4. Initial OS setup: SSH key + Docker

In the VM:

```bash
# (Optional but pleasant) — push your Windows SSH public key from the host:
# On the Windows host:
#   ssh-copy-id aethernet@192.168.x.x   # if you have ssh-copy-id (Git Bash / WSL)
# or just:
#   type $env:USERPROFILE\.ssh\id_rsa.pub | ssh aethernet@192.168.x.x "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys"

# Install Docker Engine + Compose plugin (Ubuntu 26.04 — codename will show in lsb_release):
sudo apt-get update
sudo apt-get install -y ca-certificates curl gnupg
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | \
    sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
# $VERSION_CODENAME comes from /etc/os-release. If Docker's repo doesn't yet list 26.04's
# codename, swap the variable below for `noble` (24.04 packages work fine on 26.04).
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
    https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" | \
    sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

# Run docker without sudo
sudo usermod -aG docker $USER
newgrp docker

docker --version
docker compose version
```

## 5. Create the Cloudflare Tunnel

In a browser, go to <https://one.dash.cloudflare.com> (Cloudflare Zero Trust). If
this is your first visit, walk through the free-tier signup (no card unless you go
past 50 users, which you absolutely won't).

Inside Zero Trust:

1. **Networks** → **Tunnels** → **Create a tunnel**
2. Connector type: **Cloudflared** → Next
3. Tunnel name: `aethernet-prod` → Save tunnel
4. **Install connector** screen → **Docker** tab → you'll see a long `docker run` line ending in `--token eyJh...`. Copy the **token only** (the part after `--token`). That goes in `.env` on the VM.

Don't run that docker command — our compose file already wires up the cloudflared
container; we just need the token.

5. Click **Next** to go to **Public Hostnames**. Add three of them:

   | Subdomain | Domain        | Service type | URL          |
   |-----------|---------------|--------------|--------------|
   | `auth.aethernet`  | heimiyano.com | HTTP | `auth:8080`  |
   | `hub.aethernet`   | heimiyano.com | HTTP | `hub:8080`   |
   | `files.aethernet` | heimiyano.com | HTTP | `files:8080` |

   For each: select **Add a public hostname** → fill in **Subdomain** and pick the
   **Domain** → leave Path blank → **Service**: HTTP, URL is the container name + port.

   The container names (`auth`, `hub`, `files`) resolve because cloudflared is on
   the same docker network in the compose stack.

   **For the hub specifically**, expand **Additional application settings** → 
   **TLS** section is fine at defaults → expand **HTTP Settings** → enable
   **WebSocket connections** (SignalR uses these). Save.

6. Save tunnel. Cloudflare auto-creates three orange-cloud CNAME records in your
   DNS for the subdomains, pointing at the tunnel.

## 6. Clone repo and bring up the stack

On the VM:

```bash
sudo apt-get install -y git
git clone https://github.com/YOUR_USERNAME/aethernet-sync.git ~/aethernet
# Or scp the project up:
#   On Windows:  scp -r . aethernet@192.168.x.x:~/aethernet

cd ~/aethernet/deploy
cp .env.hyperv.example .env
nano .env
```

Fill in:

- `POSTGRES_PASSWORD` — `openssl rand -base64 32`
- `JWT_SIGNING_KEY`   — `openssl rand -base64 48`
- `CLOUDFLARE_TUNNEL_TOKEN` — paste the token you copied in step 5
- Leave Discord blank unless you want to set up OAuth

Save (Ctrl-O, Enter, Ctrl-X). Now:

```bash
docker compose -f docker-compose.hyperv.yml --env-file .env up -d --build
```

First build downloads the .NET SDK image (~1 GB) and takes 5–10 min. Subsequent
rebuilds are seconds.

Check everything's up:

```bash
docker compose -f docker-compose.hyperv.yml ps
docker compose -f docker-compose.hyperv.yml logs -f cloudflared
```

In the cloudflared log, within ~10 seconds you should see:

```
INF Registered tunnel connection connIndex=0 ... location=...
```

Back in the Cloudflare dashboard, the tunnel will go from "Inactive" to "Healthy"
with four active connectors (Cloudflare opens one per nearest edge data center).

## 7. Apply the database migration

```bash
docker run --rm --network aethernet-hyperv_default \
  -e ConnectionStrings__Default="Host=postgres;Database=aethernet;Username=aethernet;Password=$(grep POSTGRES_PASSWORD .env | cut -d= -f2)" \
  -v ~/aethernet:/src -w /src \
  mcr.microsoft.com/dotnet/sdk:8.0 \
  bash -c "dotnet tool install -g dotnet-ef --version 8.* && export PATH=\$PATH:/root/.dotnet/tools && \
           dotnet ef database update --project src/Aethernet.Data --startup-project src/Aethernet.Server"
```

Verify:

```bash
docker compose -f docker-compose.hyperv.yml exec postgres \
    psql -U aethernet -d aethernet -c '\dt'
```

You should see the `users`, `pairs`, `groups`, etc. tables.

## 8. Smoke-test from your laptop (not the VM)

```powershell
# Auth: should return JSON with UID + SecretKey + RecoverySecret
curl.exe -X POST https://auth.aethernet.heimiyano.com/auth/register `
  -H "Content-Type: application/json" -d "{}"

# Hub: protocolVersion + build + motd
curl.exe https://hub.aethernet.heimiyano.com/api/info

# File server: "Healthy"
curl.exe https://files.aethernet.heimiyano.com/healthz
```

All three return 200 over HTTPS. The TLS cert is Cloudflare's edge cert — your
browser will see the orange-cloud version of Cloudflare's certificate, no Let's
Encrypt needed.

## 9. Reconfigure the plugin to point at production

On each player's machine, in-game: `/aethernet settings` → Account section:

| Field           | Value                                          |
|-----------------|------------------------------------------------|
| Auth server URL | `https://auth.aethernet.heimiyano.com`         |
| Hub server URL  | `https://hub.aethernet.heimiyano.com`          |
| File server URL | `https://files.aethernet.heimiyano.com`        |

Save → reconnect. Green dot reappears within a few seconds. Your friend installs
the plugin (`Copy-Item src\Aethernet.Plugin\bin\Release\* $env:APPDATA\XIVLauncher\devPlugins\Aethernet`)
and points at the same URLs.

---

## Operational quick reference

```bash
cd ~/aethernet/deploy

# Tail logs
docker compose -f docker-compose.hyperv.yml logs -f

# One service at a time
docker compose -f docker-compose.hyperv.yml logs -f hub

# Rebuild after pulling new code
cd ~/aethernet && git pull
docker compose -f deploy/docker-compose.hyperv.yml up -d --build hub

# Stop the stack
docker compose -f deploy/docker-compose.hyperv.yml down

# Stop + delete all data (DESTRUCTIVE — wipes users, blobs, everything)
docker compose -f deploy/docker-compose.hyperv.yml down -v
```

**Auto-start on host boot**

Set the Hyper-V VM to start with the host: Hyper-V Manager → right-click VM →
**Settings** → **Automatic Start Action** → **Always start this virtual machine
automatically**. Docker inside the VM is already enabled at boot
(`systemctl is-enabled docker` should show `enabled`) and compose's
`restart: unless-stopped` brings the containers back.

**Daily Postgres backup to a host folder**

On the VM:

```bash
sudo tee /etc/cron.daily/aethernet-pgdump <<'EOF'
#!/bin/bash
set -e
cd /home/aethernet/aethernet/deploy
mkdir -p /home/aethernet/backups
docker compose -f docker-compose.hyperv.yml exec -T postgres \
    pg_dump -U aethernet aethernet | gzip > /home/aethernet/backups/aethernet-$(date +%F).sql.gz
# keep last 30 days
find /home/aethernet/backups -name 'aethernet-*.sql.gz' -mtime +30 -delete
EOF
sudo chmod +x /etc/cron.daily/aethernet-pgdump
```

To pull the backups to your Windows host occasionally:

```powershell
scp aethernet@192.168.x.x:/home/aethernet/backups/* C:\Backups\aethernet\
```

## Common issues

**Tunnel won't connect — cloudflared log shows "Unauthorized: Failed to get tunnel"**
→ `CLOUDFLARE_TUNNEL_TOKEN` is wrong or truncated. The token is a long JWT
(~200 chars). Paste it again carefully; don't include the `--token` literal.

**Smoke tests return 502 Bad Gateway** → cloudflared is connected but can't reach
the container. Check `docker compose ps` — all three of auth/hub/files should be
"Up". Check container logs for startup errors.

**WebSocket connection fails — hub stays Disconnected in the plugin** → WebSocket
support isn't enabled on the hub's public hostname in Cloudflare. Go back to
Zero Trust → Networks → Tunnels → your tunnel → Public Hostnames → edit the hub
entry → Additional application settings → HTTP Settings → ✅ WebSocket connections.

**Plugin can register but can't pull mod blobs** → JWT clock skew. The VM's clock
must be in sync. Confirm with `timedatectl` — should show "System clock synchronized: yes".
If not, `sudo systemctl restart systemd-timesyncd`.

**Host machine sleeps and the tunnel goes down** → expected. Either disable sleep
on the host (Settings → System → Power & battery → Sleep → Never) or move the VM
to an always-on box.

**VHDX is filling up** → blobs grow with usage. Shrink/expand the VHDX in Hyper-V
Manager (VM has to be off), or migrate the blob volume to a second disk. The
janitor service (`OrphanBlobJanitor`) deletes blobs no longer referenced by any
user — verify it's running with `docker compose logs files | grep janitor`.

## When to graduate off this layout

The shopping list:

- **Friends complain about latency** → tunnels add ~30 ms; for low-latency you'd
  switch to direct exposure (move to AWS docker-compose.prod.yml, or open ports on
  your home router + Let's Encrypt).
- **You want zero-downtime when you reboot the host** → that's what AWS exists for;
  use docker-compose.prod.yml on a t4g.small.
- **You hit the 50-user limit on Cloudflare Zero Trust free tier** → that's a
  Zero Trust limit, not a Tunnel limit; tunnels themselves are unlimited.
