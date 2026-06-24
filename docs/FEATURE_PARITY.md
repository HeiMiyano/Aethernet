# Aethernet feature status

A checklist of Aethernet's capabilities, grouped by area, with current implementation status.

**Legend:** ✅ done (code-complete) • 🟡 partial / placeholder • ⛔️ not started • 🔄 explicitly deferred

## Account & auth

| Feature                                    | Status   | Notes |
|--------------------------------------------|:--------:|-------|
| Anonymous registration (random UID + key)  | ✅       | `RegistrationService.RegisterAsync` |
| Secret-key login                           | ✅       | PBKDF2-hashed at rest |
| Discord OAuth registration                 | ✅       | `/auth/oauth/discord` via `AspNet.Security.OAuth.Discord` |
| Recovery secret                            | ✅       | Returned once at registration; `/auth/recover` |
| Secret-key rotation                        | ✅       | `/auth/secret/rotate` |
| JWT issuance + refresh                     | ✅       | 12h access / 30d refresh, rotated |
| Account deletion                           | ✅       | Hub `UserDelete`; cascade removes pairs + group memberships |
| Server-wide ban / mod actions              | ✅       | `ModerationBanUser` / `ModerationUnbanUser` hub methods; `BannedUserEntity`; audit log |
| Email or 2FA                               | ⛔️       | Out of scope for v1 |

## Pairing

| Feature                                    | Status   | Notes |
|--------------------------------------------|:--------:|-------|
| Add pair by UID                            | ✅       | `UserAddPair` |
| Remove pair                                | ✅       | `UserRemovePair` |
| Pair request states (one-sided / bidir)    | ✅       | `IndividualPairStatus` |
| Pair-level permissions (pause, no-anim, …) | ✅       | `UserPermissions` flags applied client-side |
| Profile (bio + image, NSFW flag)           | ✅       | `UserSetProfile` / `UserGetProfile` |
| Profile reporting                          | ✅       | `UserReportProfile`, queued in `ProfileReportEntity`, surfaced via `ModerationGetReports` |
| Profile preview tooltip                    | ✅       | Hover tooltip in main window + dedicated `ProfileViewerWindow` |
| Block list (hard block, not just pause)    | ✅       | `BlockEntity`, `UserBlock`/`UserUnblock`/`UserGetBlocked`, `BlockListWindow` |

## Groups (syncshells)

| Feature                                    | Status   | Notes |
|--------------------------------------------|:--------:|-------|
| Create group                               | ✅       | `GroupCreate` returns `GroupPasswordDto` (password shown once) |
| Join with password                         | ✅       | `GroupJoin` |
| Leave / delete                             | ✅       | `GroupLeave`, `GroupDelete` |
| Rotate password                            | ✅       | `GroupChangePassword` |
| Transfer ownership                         | ✅       | `GroupChangeOwnership` |
| Moderators                                 | ✅       | `GroupSetUserModerator` |
| Disable invites                            | ✅       | `GroupPermissions.DisableInvites` |
| Group default permissions                  | ✅       | `GroupUserPreferredPermissions` |
| Per-user override in group                 | ✅       | `GroupChangeUserInfo` |
| Kick / ban / unban / ban list              | ✅       | `GroupRemoveUser`, `GroupBanUser`, `GroupUnbanUser`, `GroupGetBans` |
| Clear all (owner)                          | ✅       | `GroupClearAll` |
| Member count / limit                       | ✅       | `GroupEntity.MemberLimit`, default 100 |
| Group aliases                              | ✅       | `GroupSetAlias` hub method, unique check, UI in `GroupAdminWindow` |
| Group admin UI                             | ✅       | `GroupAdminWindow` with rotate pw, alias, perms, members, bans, danger-zone |

## Mod sync — what gets pushed

| Feature                                    | Status   | Notes |
|--------------------------------------------|:--------:|-------|
| Penumbra file replacements                 | ✅       | `PenumbraIpc.GetResourcePaths`, ingested into file cache |
| Penumbra meta manipulations                | ✅       | `PenumbraIpc.GetMetaManipulations` |
| Penumbra file swaps (path → path)          | ✅       | `ObjectAppearanceDto.FileSwaps` |
| Glamourer state                            | ✅       | `GlamourerIpc.GetStateBase64` |
| Customize+ profile                         | ✅       | `CustomizePlusIpc.GetProfileJson` |
| Honorific title                            | ✅       | `HonorificIpc.GetTitleJson` |
| SimpleHeels offset                         | ✅       | `HeelsIpc.GetLocalOffsetJson` |
| Moodles status                             | ✅       | `MoodlesIpc.GetStatusJson` |
| Pet Names                                  | ✅       | `PetNamesIpc.GetLocalNamesJson` |
| Per-object sync (minion, pet, mount, …)    | ✅       | `EnumerateOwnedObjects` walks the object table, fills per-kind appearance |
| Brio pose data                             | ✅       | `CharacterDataDto.BrioData`, `BrioIpc` bridge, collected and applied via the same temp-collection pipeline |
| Active-option-aware mod walk               | ✅       | `EnumerateActiveModFiles` parses `default_mod.json` + `group_*.json` and only includes files from selected options |

## Mod sync — applying received data

| Feature                                    | Status   | Notes |
|--------------------------------------------|:--------:|-------|
| Penumbra temporary collection per peer     | ✅       | `CharacterDataApplier` creates one per UID |
| Wait until peer is visible before applying | ✅       | `VisibleUserManager` gates, applier defers |
| Tear down collection on invisibility       | ✅       | `OnInvisible` deletes collection |
| Apply Glamourer / Customize+ / Honorific / Heels / Moodles / Pet Names | ✅ | All branches present in `CharacterDataApplier.HandleAsync` |
| Apply per-object kinds (minion/pet/mount/companion) | ✅ | `ResolveActorIndex` per kind, separate `AddTemporaryMod` tag per kind, redraws all owned actors |
| Strip animations / sounds / VFX per permission | ✅   | `ApplyLocalPermissions` filters by file extension |
| Request fresh data from a peer             | ✅       | `UserRequestCharacterData` |
| Stale-version drop                         | ✅       | `_lastApplied` per UID |
| Pause syncing outside sanctuary zones      | ✅       | `ZoneObserver` + `SyncOrchestrator` checks |

## File CDN

| Feature                                    | Status   | Notes |
|--------------------------------------------|:--------:|-------|
| Content-addressed dedup                    | ✅       | SHA-1; `FileCacheEntity` row per blob |
| Quota enforcement                          | ✅       | `QuotaService`, per-user override on `UserEntity` |
| Parallel uploads (bounded)                 | ✅       | `FileTransferService.MaxParallelUploads` |
| Parallel downloads (bounded)               | ✅       | `MaxParallelDownloads` |
| Range / resumable downloads                | ✅       | `Range` header parsed in `FileController.Download` |
| Forbidden-blob list (moderation)           | ✅       | `AdminController` `/admin/files/forbid` and `/allow`, 451 response |
| Age-based + disk-pressure GC               | ✅       | `OrphanBlobJanitor` evicts blobs untouched for `MaxAgeDays` (14d default); emergency eviction by oldest-LastTouchedAt when disk usage crosses `DiskPressureHighPct` |
| LZ4 compression in transit                 | ✅       | `Aethernet.Shared.Compression.Lz4Stream` negotiated via Content-Encoding / Accept-Encoding on uploads and downloads |
| Cloudflare R2 (S3-compatible) backend      | ✅       | `S3BlobStore` works with R2 endpoint + `ForcePathStyle = true`; zero egress fees |
| Local disk backend                         | ✅       | `DiskBlobStore` for self-hosted single-VM deployments |

## Plugin UI

| Feature                                    | Status   | Notes |
|--------------------------------------------|:--------:|-------|
| Compact pair list with online/visible dots | ✅       | `MainWindow.DrawPairsTab` |
| Per-pair permission popover                | ✅       | `DrawPermissionsEditor` with presets (Full sync / Visual only / Minimal / Paused) and "Apply to all pairs" |
| Per-pair right-click menu                  | ✅       | View profile, request data, pause, remove, block |
| Hover tooltip with status + bio            | ✅       | Inline tooltip on each pair row |
| Group list with leave / join               | ✅       | `MainWindow.DrawGroupsTab` |
| Group admin window                         | ✅       | `GroupAdminWindow` |
| Block list window                          | ✅       | `BlockListWindow` |
| Profile viewer (with report button)        | ✅       | `ProfileViewerWindow` |
| Settings window                            | ✅       | `SettingsWindow` |
| Profile editor                             | ✅       | `EditProfileWindow` |
| Download/upload progress window            | ✅       | `DownloadStatusWindow` |
| In-world download progress overlay         | ✅       | `DownloadOverlay` draws per-character progress bars in the game world via `IGameGui.WorldToScreen` |
| Slash command (`/aethernet …`)             | ✅       | `UiBootstrapper.OnCommand` — settings/profile/downloads/blocks/wizard |
| First-run wizard                           | ✅       | `FirstRunWizard` — auto-opens when no credentials |
| Notification icons in nameplates           | ✅       | `NameplateDecorator` prefixes paired/visible players via `INamePlateGui` |
| Accent-color theming                       | ✅       | Accent-color picker in Settings, pushed into ImGui style each frame by `ThemeApplier` |

## Ops / deploy

| Feature                                    | Status   | Notes |
|--------------------------------------------|:--------:|-------|
| Dockerfiles per service                    | ✅       | `src/*/Dockerfile` |
| docker-compose for local dev               | ✅       | `deploy/docker-compose.yml` (postgres + redis + minio) |
| docker-compose for cloud production        | ✅       | `deploy/docker-compose.cloud.yml` (Hetzner VPS + Cloudflare R2 + Caddy + Let's Encrypt) |
| docker-compose for self-hosted             | ✅       | `deploy/docker-compose.hyperv.yml` (single VM + Cloudflare Tunnel) |
| Health endpoints                           | ✅       | `/healthz` everywhere |
| Prometheus metrics                         | ✅       | `AethernetMetrics` exposes hub connections/push/errors, auth login/refresh/registrations, file bytes & dedupe + GC. `/metrics` on all three services. Starter Grafana dashboard at `deploy/grafana/aethernet-overview.json` |
| Structured logs (Serilog)                  | ✅       | All three services use Serilog |
| EF Core migrations                         | ✅       | `scripts/init-migrations.{sh,ps1}` generates + applies the Initial migration in one command |
| Horizontal scale (Redis backplane)         | ✅       | `AddStackExchangeRedis` wired in `Program.cs` |
| Rate limiting                              | ✅       | Per-user limits on `UserPushData`, `UserAddPair`, `UserReportProfile`, `GroupCreate`, `GroupJoin`, `GroupChangePassword` |
| Per-tenant moderation tools                | ✅       | REST surface on the hub at `/admin/reports`, `/admin/users/{uid}`, `/admin/audit`, `/admin/stats` (admin/moderator only). Web dashboard implementation left to the operator |
| GitHub Actions release pipeline            | ✅       | Tag-push triggers build → zip → release → repo.json update for in-game Dalamud installer |

## Planned next

1. **Discord-first signup + bot recovery** — milestones M1-M6 in the task tracker. New users authenticate via Discord OAuth; lost-credential recovery via a bot DM with one-time claim token.
2. **Per-pair nicknames** — client-side local labels so users can identify which UID belongs to which person.
3. **Web moderation dashboard** — REST surface is in place; UI is operator-side work.

## Out of scope for v1

- **Email / 2FA** — secret-key + Discord OAuth is the supported auth.
