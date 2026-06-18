# Aethernet on AWS — single-host deployment runbook

End-to-end recipe for deploying Aethernet to one EC2 instance with public HTTPS at
`auth.aethernet.heimiyano.com` / `hub.aethernet.heimiyano.com` /
`files.aethernet.heimiyano.com`, S3 for mod blobs, and Postgres + Redis as containers
on the same host. Target cost: **~$14–15/mo** in `us-east-1`.

This is the v1 single-box layout. To scale later, lift Postgres to RDS and the three
.NET services to ECS Fargate behind an ALB — the docker-compose file maps 1:1.

## What you'll do, in order

1. AWS account / billing alarm
2. Create the S3 bucket
3. Create the IAM instance role (lets EC2 access S3 without static keys)
4. Launch the EC2
5. Allocate + attach an Elastic IP
6. Open security group ports 22 / 80 / 443
7. Point DNS at the Elastic IP
8. SSH in, install Docker, pull the repo, fill in `.env`, `docker compose up`
9. Confirm certs landed, smoke-test the three endpoints
10. Reconfigure the local Aethernet plugin to point at the prod URLs

Total time from a clean AWS account: ~60–90 minutes.

---

## 1. AWS account + billing alarm

If you don't already have one, create at <https://signup.aws.amazon.com>. Use a long
password, enable MFA on the root user, then create an IAM user for yourself instead
of using root day-to-day.

**Critical:** set a billing alarm before launching anything.

- Billing → **Billing preferences** → check "Receive Free Tier usage alerts" and
  "Receive Billing alerts" → save.
- CloudWatch → **Billing** → **Create alarm** → metric `EstimatedCharges` (currency:
  USD) → threshold `> $20` → SNS topic → enter your email → confirm subscription via
  the email AWS sends. Now you'll know if anything runs away.

## 2. S3 bucket for mod blobs

Console → **S3** → **Create bucket**:

- **Name**: `aethernet-blobs-heimiyano` (must be globally unique; pick anything
  available — write down what you choose, you'll put it in `.env` later).
- **Region**: US East (N. Virginia) — `us-east-1`.
- **Block all public access**: ✅ leave on. Mod blobs are served through the
  FileServer with JWT auth; the bucket itself never needs to be public.
- Versioning, encryption defaults are fine.
- Create.

## 3. IAM instance role (best practice — no static AWS keys on disk)

Console → **IAM** → **Roles** → **Create role**:

- Trusted entity: AWS service → use case **EC2** → Next.
- Permissions: skip for now → Next.
- Role name: `AethernetEC2`. Description: "Allow Aethernet EC2 to access its S3 bucket."
- Create role.

Now attach a least-privilege inline policy:

- Open the `AethernetEC2` role → **Permissions** tab → **Add permissions** →
  **Create inline policy** → JSON tab → paste:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "s3:GetObject",
        "s3:PutObject",
        "s3:DeleteObject",
        "s3:ListBucket",
        "s3:HeadBucket",
        "s3:GetBucketLocation"
      ],
      "Resource": [
        "arn:aws:s3:::aethernet-blobs-heimiyano",
        "arn:aws:s3:::aethernet-blobs-heimiyano/*"
      ]
    }
  ]
}
```

Replace `aethernet-blobs-heimiyano` if you picked a different bucket name. Name the
policy `AethernetS3Access`, save.

## 4. Launch the EC2

Console → **EC2** → **Launch instances**:

- **Name**: `aethernet-prod`
- **AMI**: Ubuntu Server 24.04 LTS (HVM), **arm64** — make sure you select the ARM
  variant; the AMI ID changes by region but it'll say "arm64" in the description.
- **Instance type**: `t4g.small` (2 vCPU ARM, 2 GB RAM). About $12.26/mo.
- **Key pair**: create a new one if you don't have one; download the `.pem` and keep
  it safe (chmod 400). You'll SSH with `ssh -i path\to\key.pem ubuntu@<elastic-ip>`.
  On Windows / PowerShell, use OpenSSH (built in to Win10+) or PuTTY.
- **Network**: default VPC is fine.
- **Firewall (security group)**: create a new one named `aethernet-prod-sg` with
  these inbound rules:
  - SSH (22) — Source: My IP (recommended) or 0.0.0.0/0 if you'll connect from
    multiple networks
  - HTTP (80) — Source: 0.0.0.0/0
  - HTTPS (443) — Source: 0.0.0.0/0
  - **Custom UDP** port 443 — Source: 0.0.0.0/0 (for HTTP/3; optional but nice)
- **Storage**: 20 GB gp3 (default 8 GB is too tight once Docker images and Postgres
  data grow). Add ~$1.60/mo.
- **Advanced details → IAM instance profile**: select `AethernetEC2`.
- Launch instance.

## 5. Elastic IP

EC2 console → **Elastic IPs** → **Allocate Elastic IP address** → defaults → allocate.

Then **Actions → Associate Elastic IP address** → pick the `aethernet-prod` instance
→ associate. Copy the IP — you'll need it for DNS in the next step.

Elastic IPs are free as long as they're attached to a running instance. If you stop
the instance for more than a day, AWS starts charging $0.005/hr (~$3.65/mo) for the
unused IP.

## 6. DNS A records at your registrar (Namecheap/etc.)

You picked "registrar DNS." In your registrar's DNS panel for `heimiyano.com`, add
three A records:

| Type | Host                          | Value (Elastic IP)  | TTL  |
|------|-------------------------------|---------------------|------|
| A    | `auth.aethernet`              | (your Elastic IP)   | 300  |
| A    | `hub.aethernet`               | (your Elastic IP)   | 300  |
| A    | `files.aethernet`             | (your Elastic IP)   | 300  |

(In most registrar UIs you only enter the subdomain part; they append `heimiyano.com`
automatically. So just `auth.aethernet`, `hub.aethernet`, `files.aethernet`.)

Verify propagation in a fresh terminal:

```bash
nslookup auth.aethernet.heimiyano.com
nslookup hub.aethernet.heimiyano.com
nslookup files.aethernet.heimiyano.com
```

All three should resolve to your Elastic IP. Usually <5 minutes; can take up to an
hour in pathological cases.

## 7. SSH in and install Docker

```bash
ssh -i path\to\key.pem ubuntu@<elastic-ip>
```

Once on the host:

```bash
# Install Docker Engine + Compose plugin
sudo apt-get update
sudo apt-get install -y ca-certificates curl gnupg
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | \
    sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
    https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" | \
    sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

# Add yourself to docker group so you don't need sudo for `docker`
sudo usermod -aG docker ubuntu
newgrp docker  # or just log out + back in

docker --version
docker compose version
```

## 8. Clone the repo and configure

```bash
sudo apt-get install -y git
git clone https://github.com/YOUR_USERNAME/aethernet-sync.git ~/aethernet
# Or: scp the project up if you haven't pushed to a remote yet.
# scp -r -i path\to\key.pem . ubuntu@<elastic-ip>:~/aethernet

cd ~/aethernet/deploy
cp .env.example .env
nano .env
```

Fill in:

- `POSTGRES_PASSWORD` — `openssl rand -base64 32`
- `JWT_SIGNING_KEY`   — `openssl rand -base64 48`
- `DOMAIN_AUTH`, `DOMAIN_HUB`, `DOMAIN_FILES` — should already match the defaults if
  you used `heimiyano.com`; verify.
- `ACME_EMAIL` — a real address you check; Let's Encrypt sends expiry warnings here.
- `S3_BUCKET` — whatever you named the bucket in step 2.
- Leave `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` blank — the IAM instance role
  from step 3 handles auth automatically.
- `DISCORD_*` — leave blank for now; the auth service skips Discord registration if
  these are empty.

Save (Ctrl-O, Enter, Ctrl-X).

## 9. Bring the stack up

```bash
cd ~/aethernet/deploy
docker compose -f docker-compose.prod.yml --env-file .env up -d --build
```

First build takes 5–10 minutes (downloading the .NET SDK image is the slow part).
Subsequent rebuilds are seconds.

Check everything's up:

```bash
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs -f caddy
```

Watch the Caddy logs — within ~30 seconds of all three DNS records being live, you'll
see lines like:

```
{"level":"info","logger":"tls.obtain","msg":"obtaining certificate","identifier":"auth.aethernet.heimiyano.com"}
{"level":"info","logger":"tls.obtain","msg":"certificate obtained successfully"}
```

If Caddy can't fetch a cert it logs the reason clearly — most often it's that DNS
hasn't propagated yet. Wait 60 seconds and Caddy retries automatically.

## 10. Apply the database migration

The hub container is the EF startup project, so run it from there:

```bash
docker compose -f docker-compose.prod.yml exec hub \
    dotnet ef database update --no-build
```

If this errors because the tooling isn't in the runtime image, run it from outside
the container instead:

```bash
docker run --rm --network aethernet-prod_default \
  -e ConnectionStrings__Default="Host=postgres;Database=aethernet;Username=aethernet;Password=$(grep POSTGRES_PASSWORD .env | cut -d= -f2)" \
  -v ~/aethernet:/src -w /src \
  mcr.microsoft.com/dotnet/sdk:8.0 \
  bash -c "dotnet tool install -g dotnet-ef --version 8.* && export PATH=\$PATH:/root/.dotnet/tools && \
           dotnet ef database update --project src/Aethernet.Data --startup-project src/Aethernet.Server"
```

Verify the schema landed:

```bash
docker compose -f docker-compose.prod.yml exec postgres \
    psql -U aethernet -d aethernet -c '\dt'
```

You should see `users`, `pairs`, `groups`, etc.

## 11. Smoke-test the public endpoints

From your laptop, not the EC2:

```powershell
# Should print JSON: { "UID": "u-...", "SecretKey": "...", "RecoverySecret": "..." }
curl.exe -X POST https://auth.aethernet.heimiyano.com/auth/register `
  -H "Content-Type: application/json" -d "{}"

# Should print: { "protocolVersion": 1, "build": "0.1.0.0", "motd": "Welcome to Aethernet." }
curl.exe https://hub.aethernet.heimiyano.com/api/info

# Should print: Healthy
curl.exe https://files.aethernet.heimiyano.com/healthz
```

All three should return 200 with the expected body, over HTTPS, with a valid Let's
Encrypt cert. If any fail, check `docker compose logs -f <service>` on the EC2 for
the trace.

## 12. Reconfigure the plugin to point at production

In-game on each player's client: `/aethernet settings` → Account section → replace
the three URLs:

| Field           | Value                                          |
|-----------------|------------------------------------------------|
| Auth server URL | `https://auth.aethernet.heimiyano.com`         |
| Hub server URL  | `https://hub.aethernet.heimiyano.com`          |
| File server URL | `https://files.aethernet.heimiyano.com`        |

Save. Click **Reconnect** in Settings or restart the plugin from `/xlplugins`. The
green dot should reappear within a few seconds, and the smoke tests in `TESTING.md`
sections 5–8 should now work between two clients on different machines.

---

## Operational quick reference

```bash
# Tail all logs
cd ~/aethernet/deploy && docker compose -f docker-compose.prod.yml logs -f

# Restart one service after pulling new code
cd ~/aethernet && git pull
docker compose -f deploy/docker-compose.prod.yml up -d --build hub  # or auth/files

# Take the whole stack down
docker compose -f deploy/docker-compose.prod.yml down

# Take it down AND delete the volumes (DESTRUCTIVE)
docker compose -f deploy/docker-compose.prod.yml down -v
```

**Daily Postgres backup → S3** (recommended, ~10 min to set up):

```bash
sudo tee /etc/cron.daily/aethernet-pgdump <<'EOF'
#!/bin/bash
set -e
cd /home/ubuntu/aethernet/deploy
docker compose -f docker-compose.prod.yml exec -T postgres \
    pg_dump -U aethernet aethernet | gzip > /tmp/aethernet-$(date +%F).sql.gz
aws s3 cp /tmp/aethernet-*.sql.gz s3://$(grep S3_BUCKET .env | cut -d= -f2)/backups/
rm /tmp/aethernet-*.sql.gz
EOF
sudo chmod +x /etc/cron.daily/aethernet-pgdump
```

Add `s3:PutObject` on `arn:aws:s3:::your-bucket/backups/*` to the IAM role if you
narrowed the policy beyond what step 3 covers.

## Common issues

**Caddy can't fetch certificates** — almost always DNS. Check with `dig` from a
non-AWS network. Also: Let's Encrypt rate-limits to 5 failures per hour per domain;
during initial testing, uncomment the `acme_ca` line in `Caddyfile` to use the
staging CA so retries are unlimited.

**S3 access denied from the files container** — IAM role didn't attach, or the
inline policy's bucket name doesn't match `S3_BUCKET` in `.env`. Verify with
`aws sts get-caller-identity` from inside the files container.

**Hub connections drop after 60s** — the `read_timeout` / `write_timeout` lines in
the `Caddyfile` weren't applied. Re-check the file and `docker compose restart caddy`.

**Plugin can connect but never sees pair updates** — websocket isn't upgrading.
Inspect `wscat -c wss://hub.aethernet.heimiyano.com/aethernet-hub` from your laptop;
if it errors with "Unexpected server response: 200" instead of 101, Caddy isn't
forwarding the Upgrade header — usually a Caddyfile typo on the `header_up` lines.

## When to graduate off this layout

The shopping list of pain points that mean it's time to migrate piece by piece:

- **Postgres I/O is bottlenecking your hub at peak** → RDS db.t4g.small with provisioned IOPS.
- **The EC2 box runs out of RAM during heavy mod-pull events** → split files/ off to
  its own t4g.small, or just bump up to t4g.medium.
- **You want zero-downtime deploys** → ECS Fargate with two tasks per service behind
  an ALB. The `docker-compose.prod.yml` translates cleanly to a task definition.
