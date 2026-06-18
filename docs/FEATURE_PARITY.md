# Feature parity with Mare Synchronos

This document is the running checklist for "is Aethernet a real replacement?" Each row maps a Mare feature to Aethernet's current status.

**Legend:** ✅ done (scaffolded and code-complete in this branch) • 🟡 partial / placeholder • ⛔️ not started • 🔄 explicitly deferred

> Reminder: "done" here means "code exists and compiles." None of this has been tested against a live FFXIV client; that's the work of the next phase.

## Account & auth

| Mare feature                              | Aethernet | Notes |
|-------------------------------------------|:---------:|-------|
| Anonymous registration (random UID + key) | ✅        | `RegistrationService.RegisterAsync` |
| Secret-key login                          | ✅        | PBKDF2-hashed at rest |
| Discord OAuth registration                | ✅        | `/auth/oauth/discord` via `AspNet.Security.OAuth.Discord` |
| Recovery secret                           | ✅        | Returned once at registration; `/auth/recover` |
| Secret-key rotation                       | ✅        | `/auth/secret/rotate` |
| JWT issuance + refresh                    | ✅        | 12h access / 30d refresh, rotated |
| Account deletion                          | ✅        | Hub `UserDelete`; cascade removes pairs + group memberships |
| Server-wide ban / mod actions             | ✅        | `ModerationBanUser` / `ModerationUnbanUser` hub methods; `BannedUserEntity`; audit log |
| Email or 2FA                              | ⛔️        | Out of scope for v1 |

## Pairing

| Mare feature                              | Aethernet | Notes |
|-------------------------------------------|:---------:|-------|
| Add pair by UID                           | ✅        | `UserAddPair` |
| Remove pair                               | ✅        | `UserRemovePair` |
| Pair request states (one-sided / bidir)   | ✅        | `IndividualPairStatus` |
| Pair-level permissions (pause, no-anim, …)| ✅        | `UserPermissions` flags applied client-side |
| Profile (bio + image, NSFW flag)          | ✅        | `UserSetProfile` / `UserGetProfile` |
| Profile reporting                         | ✅        | `UserReportProfile`, queued in `ProfileReportEntity`, surfaced via `ModerationGetReports` |
| Profile preview tooltip                   | ✅        | Hover tooltip in main window + dedicated `ProfileViewerWindow` |
| Block list (hard block, not just pause)   | ✅        | `BlockEntity`, `UserBlock`/`UserUnblock`/`UserGetBlocked`, `BlockListWindow` |

## Groups (syncshells)

| Mare feature                              | Aethernet | Notes |
|-------------------------------------------|:---------:|-------|
| Create group                              | ✅        | `GroupCreate` returns `GroupPasswordDto` (password shown once) |
| Join with password                        | ✅        | `GroupJoin` |
| Leave / delete                            | ✅        | `GroupLeave`, `GroupDelete` |
| Rotate password                           | ✅        | `GroupChangePassword` |
| Transfer ownership                        | ✅        | `GroupChangeOwnership` |
| Moderators                                | ✅        | `GroupSetUserModerator` |
| Disable invites                           | ✅        | `GroupPermissions.DisableInvites` |
| Group default permissions                 | ✅        | `GroupUserPreferredPermissions` |
| Per-user override in group                | ✅        | `GroupChangeUserInfo` |
| Kick / ban / unban / ban list             | ✅        | `GroupRemoveUser`, `GroupBanUser`, `GroupUnbanUser`, `GroupGetBans` |
| Clear all (owner)                         | ✅        | `GroupClearAll` |
| Member count / limit                      | ✅        | `GroupEntity.MemberLimit`, default 100 |
| Group aliases                             | ✅        | `GroupSetAlias` hub method, unique check, UI in `GroupAdminWindow` |
| Group admin UI                            | ✅        | `GroupAdminWindow` with rotate pw, alias, perms, members, bans, danger-zone |

## Mod sync — what gets pushed

| Mare feature                              | Aethernet | Notes |
|-------------------------------------------|:---------:|-------|
| Penumbra file replacements                | ✅        | `PenumbraIpc.GetResourcePaths`, ingested into file cache |
| Penumbra meta manipulations               | ✅        | `PenumbraIpc.GetMetaManipulations` |
| Penumbra file swaps (path → path)         | ✅        | `ObjectAppearanceDto.FileSwaps` |
| Glamourer state                           | ✅        | `GlamourerIpc.GetStateBase64` |
| Customize+ profile                        | ✅        | `CustomizePlusIpc.GetProfileJson` |
| Honorific title                           | ✅        | `HonorificIpc.GetTitleJson` |
| SimpleHeels offset                        | ✅        | `HeelsIpc.GetLocalOffsetJson` |
| Moodles status                            | ✅        | `MoodlesIpc.GetStatusJson` |
| Pet Names                                 | ✅        | `PetNamesIpc.GetLocalNamesJson` |
| Per-object sync (minion, pet, mount, …)   | ✅        | `EnumerateOwnedObjects` walks the object table, fills per-kind appearance |
| Voice / Brio                              | ✅        | `CharacterDataDto.BrioData`, `BrioIpc` bridge, collected and applied via the same temp-collection pipeline |

## Mod sync — applying received data

| Mare feature                              | Aethernet | Notes |
|-------------------------------------------|:---------:|-------|
| Penumbra temporary collection per peer    | ✅        | `CharacterDataApplier` creates one per UID |
| Wait until peer is visible before applying| ✅        | `VisibleUserManager` gates, applier defers |
| Tear down collection on invisibility      | ✅        | `OnInvisible` deletes collection |
| Apply Glamourer / Customize+ / Honorific / Heels / Moodles / Pet Names | ✅ | All branches present in `CharacterDataApplier.HandleAsync` |
| Apply per-object kinds (minion/pet/mount/companion) | ✅ | `ResolveActorIndex` per kind, separate `AddTemporaryMod` tag per kind, redraws all owned actors |
| Strip animations / sounds / VFX per permission | ✅   | `ApplyLocalPermissions` filters by file extension |
| Request fresh data from a peer            | ✅        | `UserRequestCharacterData` |
| Stale-version drop                        | ✅        | `_lastApplied` per UID |
| Pause syncing outside sanctuary zones     | ✅        | `ZoneObserver` + `SyncOrchestrator` checks |

## File CDN

| Mare feature                              | Aethernet | Notes |
|-------------------------------------------|:---------:|-------|
| Content-addressed dedup                   | ✅        | SHA-1; `FileCacheEntity` row per blob |
| Quota enforcement                         | ✅        | `QuotaService`, per-user override on `UserEntity` |
| Parallel uploads (bounded)                | ✅        | `FileTransferService.MaxParallelUploads` |
| Parallel downloads (bounded)              | ✅        | `MaxParallelDownloads` |
| Range / resumable downloads               | ✅        | `Range` header parsed in `FileController.Download` |
| Forbidden-blob list (moderation)          | ✅        | `AdminController` `/admin/files/forbid` and `/allow`, 451 response |
| Garbage collection of orphaned blobs      | ✅        | `OrphanBlobJanitor` hosted service: flag → wait grace → delete |
| LZ4 compression in transit                | ✅        | `Aethernet.Shared.Compression.Lz4Stream` negotiated via Content-Encoding / Accept-Encoding on uploads and downloads |

## Plugin UI

| Mare feature                              | Aethernet | Notes |
|-------------------------------------------|:---------:|-------|
| Compact pair list with online/visible dots| ✅        | `MainWindow.DrawPairsTab` |
| Per-pair permission popover               | ✅        | `DrawPermissionsEditor` |
| Per-pair right-click menu                 | ✅        | View profile, request data, pause, remove, block |
| Hover tooltip with status + bio           | ✅        | Inline tooltip on each pair row |
| Group list with leave / join              | ✅        | `MainWindow.DrawGroupsTab` |
| Group admin window                        | ✅        | `GroupAdminWindow` |
| Block list window                         | ✅        | `BlockListWindow` |
| Profile viewer (with report button)       | ✅        | `ProfileViewerWindow` |
| Settings window                           | ✅        | `SettingsWindow` |
| Profile editor                            | ✅        | `EditProfileWindow` |
| Download/upload progress window           | ✅        | `DownloadStatusWindow` |
| Slash command (`/aethernet …`)            | ✅        | `UiBootstrapper.OnCommand` — settings/profile/downloads/blocks/wizard |
| First-run wizard                          | ✅        | `FirstRunWizard` — auto-opens when no credentials |
| Notification icons in nameplates          | ✅        | `NameplateDecorator` prefixes paired/visible players via `INamePlateGui` |
| Theming / DPI scaling                     | ✅        | Accent-color picker + UI-scale slider in Settings, pushed into ImGui style each frame by `ThemeApplier` |

## Ops / deploy

| Mare feature                              | Aethernet | Notes |
|-------------------------------------------|:---------:|-------|
| Dockerfiles per service                   | ✅        | `src/*/Dockerfile` |
| docker-compose for local dev              | ✅        | `deploy/docker-compose.yml` (postgres + redis + minio) |
| Health endpoints                          | ✅        | `/healthz` everywhere |
| Prometheus metrics                        | ✅        | `AethernetMetrics` exposes hub connections/push/errors, auth login/refresh/registrations, file bytes & dedupe + GC. `/metrics` on all three services. Starter Grafana dashboard at `deploy/grafana/aethernet-overview.json` |
| Structured logs (Serilog)                 | ✅        | All three services use Serilog |
| EF Core migrations                        | ✅        | `scripts/init-migrations.{sh,ps1}` generates + applies the Initial migration in one command |
| Horizontal scale (Redis backplane)        | ✅        | `AddStackExchangeRedis` wired in `Program.cs` |
| Rate limiting                             | ✅        | Per-user limits on `UserPushData`, `UserAddPair`, `UserReportProfile`, `GroupCreate`, `GroupJoin`, `GroupChangePassword` |
| Orphan-blob GC                            | ✅        | `OrphanBlobJanitor` hosted service |
| Per-tenant moderation tools / dashboard   | ✅        | REST surface on the hub at `/admin/reports`, `/admin/users/{uid}`, `/admin/audit`, `/admin/stats` (admin/moderator only). Web dashboard implementation left to the operator |

## Remaining gaps

Everything called out in the tables above is implemented. The unfinished bits below are intentionally out-of-scope or require something only the operator can do.

1. **Email / 2FA** — explicitly out of scope for v1; secret-key + Discord OAuth is the supported auth.
2. **End-to-end verification against a live FFXIV client** — the project compiles and the static contracts line up, but nothing has been loaded into Dalamud yet. Run through `docs/TESTING.md` once the binaries are built.
3. **IPC method-label drift** — Penumbra / Glamourer / Brio occasionally rename their published IPC labels between versions. The strings in `Aethernet.Plugin/IPC/*` mirror the contracts as of this writing; check the corresponding plugin's docs if an IPC silently no-ops.
4. **Discord OAuth final-screen UX** — now ships a styled success page, but a real deployment may want to brand it (logo + custom domain).
5. **Web moderation dashboard** — the REST surface (`/admin/reports`, `/admin/users/{uid}`, etc.) is in place. Wiring a polished HTML/JS dashboard onto it is operator work.
