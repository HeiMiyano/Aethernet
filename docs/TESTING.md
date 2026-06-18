# Testing the Aethernet plugin (client side)

This walks through validating every client surface — from "the DLL loaded" to "two accounts can actually sync mods." Server side must be up first; see [`RUNNING.md`](RUNNING.md).

## 0. Prerequisites

- **A working Dalamud install.** XIVLauncher + Dalamud, with dev plugins enabled. The Aethernet plugin currently references Dalamud assemblies from `%AppData%\XIVLauncher\addon\Hooks\dev\`.
- **Penumbra and Glamourer installed and configured.** Customize+, Honorific, SimpleHeels, Moodles, and Pet Names are optional — Aethernet syncs whatever's present.
- **At least one mod active in Penumbra** that affects your local character. Without this, there's nothing to push.
- **(Recommended) two accounts** — either two FFXIV characters on two machines, or one machine with two Aethernet identities. Single-machine testing only validates collection and upload; you need two endpoints to see "apply received data" in action.

## 1. Install the dev plugin into Dalamud

After `dotnet build src/Aethernet.Plugin -c Release`:

```bash
# Adjust the AppData path to your actual Dalamud profile.
mkdir -p "$APPDATA/XIVLauncher/devPlugins/Aethernet"
cp src/Aethernet.Plugin/bin/Release/Aethernet.Plugin.dll       "$APPDATA/XIVLauncher/devPlugins/Aethernet/"
cp src/Aethernet.Plugin/bin/Release/aethernet.json             "$APPDATA/XIVLauncher/devPlugins/Aethernet/"
# Copy all the rest of the runtime DLLs Dalamud doesn't already ship with:
cp src/Aethernet.Plugin/bin/Release/*.dll                      "$APPDATA/XIVLauncher/devPlugins/Aethernet/"
```

In-game:

1. `/xlsettings` → Experimental → make sure "Get plugin testing builds" or "Dev plugin locations" includes the folder above.
2. `/xlplugins` → Installed → look for **Aethernet** → click **Enable**.
3. If it doesn't show up, open `/xllog` and search for "Aethernet" — load errors and stack traces land there.

## 2. First-run wizard

When the plugin loads with no saved credentials, the **Welcome to Aethernet** wizard opens automatically.

| Step       | What to do                                                                                          | What to verify                                            |
|-----------:|-----------------------------------------------------------------------------------------------------|-----------------------------------------------------------|
| Welcome    | Click **Next**.                                                                                     | Reads cleanly.                                            |
| Servers    | Enter `http://localhost:5001`, `http://localhost:5002`, `http://localhost:5003`. **Next**.          | Settings persist after a `/xlplugins` reload.             |
| Account    | Click **Register**.                                                                                 | Banner shows `Registered as u-…`. `_config.Uid` is set.  |
| Pairing    | Skip for now. **Finish**.                                                                           | Wizard closes, main window can be opened.                |

If the wizard didn't auto-open (e.g. you re-installed and credentials were preserved): `/aethernet wizard`.

## 3. Slash command surface

Run each and confirm the right window opens:

| Command                | Opens                                                       |
|------------------------|-------------------------------------------------------------|
| `/aethernet`           | Main window                                                 |
| `/aethernet settings`  | Settings window                                             |
| `/aethernet profile`   | Profile editor                                              |
| `/aethernet downloads` | Transfers window                                            |
| `/aethernet blocks`    | Block list window                                           |
| `/aethernet wizard`    | First-run wizard                                            |

## 4. Connection status banner

Main window header should show a coloured dot:

- **green ● Connected** — registered, JWT valid, hub websocket open.
- **yellow ● Connecting…/Reconnecting…** — the SignalR client is mid-handshake.
- **red ● Disconnected** — credentials missing, server unreachable, or JWT rejected.

Verification:

```text
1. Stop the hub container (Ctrl-C on its dotnet run).        →  red within 30s
2. Restart it.                                                →  yellow → green within a few seconds
3. In Settings → click "Reconnect".                           →  yellow → green
```

## 5. Pairing flow (single account, two clients)

You need **two** Aethernet identities. Easiest path: register a second account from the command line.

```bash
REG=$(curl -s -X POST http://localhost:5001/auth/register -H 'Content-Type: application/json' -d '{}')
echo "$REG"     # save the UID
```

Back in-game on your real account:

1. Main window → **Pairs** tab → paste the second UID → **Add pair**.
2. The new pair shows up with `(pending)` next to it.
3. From the second identity, send a reciprocal `UserAddPair`. The pending tag disappears on both sides; the dot goes blue (online but not yet visible).

If the second identity is also logged in via another client and standing next to you in-game, the dot should turn green within ~250ms.

## 6. Permissions and pause

Right-click a pair row to open the context menu:

- **Pause / Resume** — toggles `UserPermissions.Paused`. While paused, you stop receiving (and stop applying) data from that pair, but the hub doesn't actually drop the pair.
- **View profile** — opens `ProfileViewerWindow`. Shows bio, NSFW flag, "Report profile" button.
- **Request data** — sends `UserRequestCharacterData`. The other client gets a `REQUEST_DATA:<uid>` notice in chat/log.
- **Remove pair** — `UserRemovePair`. The other side is told too.
- **Block (hard block)** — `UserBlock`. Pair severed; future `UserAddPair` from either side returns `blocked`.

Click **Perms** to open the permissions popover and toggle individual flags: `DisableAnimations`, `DisableSounds`, `DisableVfx`, `DisableHonorific`, `DisableMoodles`, `DisableHeels`, `DisablePetNames`, `DisableCustomize`. Verify that toggling `DisableAnimations` causes `.pap` files to be stripped from the next push you receive (check the plugin log).

## 7. Groups (syncshells)

1. Main window → **Groups** → **Create new syncshell**.
2. A green status line shows `created g-… pw=…` — copy the password before it scrolls.
3. From the second identity, **Groups** → paste GID, paste password → **Join**.
4. The new member shows up in both groups' member lists; they become a pair via the shared group.
5. On the owner side, click **Admin** to open `GroupAdminWindow`. Verify each section:
   - **Alias** — set `myshell`. Spot-check `\d groups` in Postgres shows the alias.
   - **Password → Rotate** — new password appears in red banner for 15 s.
   - **Permissions** — toggle `DisableInvites`; second identity can no longer join even with the password.
   - **Members → Mod / Demote** — verify role changes are echoed via `Client_GroupPairChangeUserInfo`.
   - **Members → Kick** — second identity is removed from the group.
   - **Bans → Refresh** — banned UID appears; **Unban** removes it.
   - **Danger zone → DELETE group** — hub sends `Client_GroupDelete` to everyone, both clients drop the group from the list.

## 8. Mod sync end-to-end

This is the headline feature. Two accounts, two players standing next to each other in-game.

1. On account A, ensure at least one Penumbra mod is enabled that visibly changes the character (e.g. a clothing replacement).
2. Open `/aethernet downloads` on both clients so you can watch the progress bars.
3. On account B, watch for account A's character.

What should happen:

| Phase                                | Where to look                                                       |
|--------------------------------------|---------------------------------------------------------------------|
| A collects state                     | Plugin log on A: `CharacterDataCollector` debug lines               |
| A hashes mod files into local cache  | `%AppData%\XIVLauncher\pluginConfigs\Aethernet\cache\<hashed dirs>` |
| A asks file server "which hashes?"   | File-server log: `POST /files/has 200`                              |
| A uploads missing blobs              | Transfers window on A shows upload bars; file-server log shows `POST /files/upload 200` |
| A pushes via hub                     | Hub log: `dispatch from u-… -> N recipients`                        |
| B receives `Client_UserReceiveCharacterData` | B's plugin log shows the version number                     |
| B downloads any missing blobs        | Transfers window on B; file-server log shows `GET /files/<hash>`    |
| B creates a Penumbra temp collection | Penumbra debug log: `aethernet:u-…` collection created              |
| B redraws A's character              | A visibly shows the mod on B's screen                               |

If B never redraws:

- Confirm `VisibleUserManager.IsVisible(uid)` returns true (the Pairs tab should show a green dot rather than a blue one).
- Confirm `PenumbraIpc.IsAvailable` is true (open the Penumbra debug log and check IPC version).
- Check the plugin log for `could not create Penumbra collection for u-…` — that points at a Penumbra IPC label drift.

## 9. Per-object sync (minion / pet / mount / companion)

1. Summon a minion or mount on A with a mod applied to it.
2. On B, watch the plugin log: the `Appearances` dictionary in the inbound `CharacterDataDto` should have entries for `Minion` / `Mount` / `Pet` / `Companion` alongside `Player`.
3. The applier creates a separate temporary-mod tag per kind (`aethernet:<uid>:Minion`, etc.) and redraws each owned actor. The minion should display the mod the same way as the player.

## 10. Nameplate decorations

With at least one paired player visible:

- Their nameplate **title line** should get a star/marker prefix (paired+visible).
- Pairs that are online but not visible get a different marker (paired-but-not-visible).
- Non-paired players are untouched.

If nothing changes, confirm `INamePlateGui` is wired by checking the plugin log for `NameplateDecorator` activity. (Dalamud's nameplate API changed in 2024; older Dalamud builds don't have it — update Dalamud first.)

## 11. Quiet-zone pause

1. Settings → tick **Pause when outside major cities**. Save.
2. In a city/sanctuary territory, pushes still happen (plugin log shows `dispatch`).
3. Travel to an open field (`/teleport` to a non-city aetheryte). After the territory change, pushes stop with `paused: outside quiet zone` in the plugin log.
4. Travel back. Pushes resume on the next pair change or appearance change.

If your sanctuary isn't on the list, add it to `Aethernet.Plugin.Services.ZoneObserver.SanctuaryTerritories`. The territory ID can be read from `/xllog` once you enter the zone.

## 12. Recovery and rotation

- Settings → **Account** section shows your UID and a "(set/hidden)" indicator for the secret key. Click **Rotate secret** in the wizard (or run the auth-service endpoint directly) → the old key is invalidated immediately. Restart the hub connection and confirm the new JWT works.
- Use a recovery secret you got at registration to register a "new" account — the auth service detects the recovery hash and hands back the same UID with a fresh key. Verify by logging in.

## 13. Moderation surface (if you're admin)

After promoting your account in Postgres (see RUNNING.md §8):

```bash
# Ban a UID:
curl -X POST http://localhost:5002/aethernet/...  # via hub method ModerationBanUser
# Mark a blob forbidden:
curl -X POST http://localhost:5003/admin/files/forbid \
  -H "Authorization: Bearer $JWT" -H "Content-Type: application/json" \
  -d "{\"Hash\":\"$HASH\",\"Reason\":\"copyrighted texture\"}"
```

Then re-test the file-server endpoints — `/files/has` should now include the forbidden hash in the `Forbidden` array, and `GET /files/<hash>` returns 451.

## 14. Cleanup tests

- Disable Aethernet from `/xlplugins`. The plugin's `Dispose` should disconnect the hub, dispose the HTTP client, and unhook framework events.  Re-enable and verify the connection comes back without restarting FFXIV.

## What "passes the testing checklist" looks like

- All slash commands open the right window.
- Wizard registered an account and the main banner is green.
- Two accounts can pair, group, push, receive, and visibly redraw mods on each other.
- Minions/pets/mounts get their modded appearance too.
- Pausing, blocking, unblocking, and removing pairs all propagate to the other side within ~1 s.
- Nameplate markers reflect visibility state.
- Killing the hub mid-session causes a red banner; restarting it goes back to green automatically.

If all of the above pass, you have a working Mare-equivalent end to end.
