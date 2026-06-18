# Protocol

This document is the canonical reference for the wire surface Aethernet plugins and servers speak. Method names, route paths, and DTO shapes are also embodied in code under `Aethernet.API`; this doc describes the *intent*.

## Versioning

- `AethernetConstants.ProtocolVersion` is bumped any time a breaking change is made.
- Clients send their protocol version in the SignalR query string (`?proto=N`). Mismatches cause the hub to close the connection with a `ReceiveServerMessage(Error, …)`.
- DTOs use MessagePack (`keyAsPropertyName: true`); adding optional fields is backwards-compatible, renaming or removing them is not.

## Auth service (REST)

Base URL: configurable, e.g. `https://auth.aethernet.example`.

| Method | Path                       | Body                                 | Returns                  |
|-------:|----------------------------|--------------------------------------|--------------------------|
| POST   | `/auth/register`           | `RegisterRequestDto`                 | `RegisterResponseDto`    |
| POST   | `/auth/login`              | `LoginRequestDto`                    | `LoginResponseDto`       |
| POST   | `/auth/refresh`            | `RefreshRequestDto`                  | `LoginResponseDto`       |
| POST   | `/auth/logout`             | —                                    | 204                      |
| POST   | `/auth/recover`            | `{ "recovery_secret": "…" }`         | `RegisterResponseDto`    |
| GET    | `/auth/me`                 | —                                    | `MeResponseDto`          |
| POST   | `/auth/secret/rotate`      | —                                    | `RegisterResponseDto`    |
| GET    | `/auth/oauth/discord`      | —                                    | 302 → Discord            |
| GET    | `/auth/oauth/discord/cb`   | `?code`                              | JSON `{uid, secret_key}` |

The secret key is returned **once** at registration; it is never sent back to the user again. Lose it and the only recourse is the recovery secret (also shown once).

## File server (REST)

Base URL: configurable, e.g. `https://files.aethernet.example`. All routes require `Authorization: Bearer <jwt>`.

| Method | Path                | Body                                | Returns                |
|-------:|---------------------|-------------------------------------|------------------------|
| POST   | `/files/has`        | `HasFilesRequestDto`                | `HasFilesResponseDto`  |
| POST   | `/files/upload`     | multipart: `hash`, `file`           | `FileUploadAckDto`     |
| GET    | `/files/{hash}`     | (supports `Range`)                  | bytes                  |
| DELETE | `/files/{hash}`     | —                                   | 204 / 403              |
| GET    | `/files/quota`      | —                                   | `FileQuotaDto`         |
| POST   | `/admin/files/forbid` | `{hash, reason}` (admin/mod)      | 204                    |
| POST   | `/admin/files/allow`  | `{hash, reason}` (admin/mod)      | 204                    |
| GET    | `/admin/files/stats`  | — (admin/mod)                     | `{count, totalBytes, orphans, forbidden}` |

The upload endpoint streams the body to a temp file, hashes it, and rejects the upload if the verified hash doesn't match the form field (400 `hash_mismatch`). Existing blobs are deduped — repeated uploads of the same hash return `AlreadyExisted: true`.

## Hub (SignalR, MessagePack)

Mount path: `/aethernet` (defined by `AethernetConstants.HubPath`). The plugin authenticates by passing the JWT as the `access_token` query parameter; the hub's `JwtBearerEvents.OnMessageReceived` picks it up there for the websocket upgrade.

### Method-name source of truth

Method names live in `Aethernet.API/HubMethods.cs`. Both client and server import these constants — there is no string duplication.

### Server methods (client → hub)

| Constant                              | Arguments                                | Returns                          |
|---------------------------------------|------------------------------------------|----------------------------------|
| `Heartbeat`                           | —                                        | `DateTime`                       |
| `CheckClientHealth`                   | —                                        | `bool`                           |
| `UserGetOnlinePairs`                  | —                                        | `List<OnlineUserIdentDto>`       |
| `UserGetPairedClients`                | —                                        | `List<UserPairDto>`              |
| `UserAddPair`                         | `UserDto`                                | —                                |
| `UserRemovePair`                      | `UserDto`                                | —                                |
| `UserSetPairPermissions`              | `UserPermissionsDto`                     | —                                |
| `UserBlock`                           | `UserDto`, `string?` reason              | —                                |
| `UserUnblock`                         | `UserDto`                                | —                                |
| `UserGetBlocked`                      | —                                        | `List<UserDto>`                  |
| `UserPushData`                        | `UserCharaDataMessageDto`                | —                                |
| `UserRequestCharacterData`            | `UserDto`                                | —                                |
| `UserGetProfile`                      | `UserDto`                                | `UserProfileDto`                 |
| `UserSetProfile`                      | `UserProfileDto`                         | —                                |
| `UserReportProfile`                   | `UserProfileReportDto`                   | —                                |
| `UserDelete`                          | —                                        | —                                |
| `GroupCreate`                         | —                                        | `GroupPasswordDto`               |
| `GroupJoin`                           | `GroupPasswordDto`                       | `GroupFullInfoDto`               |
| `GroupLeave`                          | `GroupDto`                               | —                                |
| `GroupDelete`                         | `GroupDto`                               | —                                |
| `GroupChangePassword`                 | `GroupDto`                               | `GroupPasswordDto`               |
| `GroupSetPermissions`                 | `GroupDto`, `GroupPermissions`           | —                                |
| `GroupChangeOwnership`                | `GroupDto`, `UserDto`                    | —                                |
| `GroupChangeUserInfo`                 | `GroupPairUserInfoDto`                   | —                                |
| `GroupRemoveUser`                     | `GroupDto`, `UserDto`                    | —                                |
| `GroupBanUser` / `GroupUnbanUser`     | `GroupDto`, `UserDto`, `string?`         | —                                |
| `GroupGetBans`                        | `GroupDto`                               | `List<UserDto>`                  |
| `GroupClearAll`                       | `GroupDto`                               | —                                |
| `GroupSetUserModerator`               | `GroupDto`, `UserDto`, `bool`            | —                                |
| `GroupSetAlias`                       | `GroupDto`, `string?`                    | —                                |
| `ModerationBanUser`                   | `UserDto`, `string?` reason              | — (admin/moderator only)         |
| `ModerationUnbanUser`                 | `UserDto`                                | — (admin/moderator only)         |
| `ModerationGetReports`                | —                                        | `List<UserProfileReportDto>` (admin/moderator only) |

### Client methods (hub → client)

| Constant                              | Arguments                       |
|---------------------------------------|---------------------------------|
| `Client_ReceiveServerMessage`         | `ServerMessageDto`              |
| `Client_UserSendOnline`               | `OnlineUserIdentDto`            |
| `Client_UserSendOffline`              | `UserDto`                       |
| `Client_UserAddPair`                  | `UserPairDto`                   |
| `Client_UserRemovePair`               | `string` (uid)                  |
| `Client_UserUpdatePairPermissions`    | `UserPermissionsDto`            |
| `Client_UserUpdateProfile`            | `UserProfileDto`                |
| `Client_UserReceiveCharacterData`     | `OnlineUserCharaDataMessageDto` |
| `Client_GroupSendFullInfo`            | `GroupFullInfoDto`              |
| `Client_GroupSendInfo`                | `GroupInfoDto`                  |
| `Client_GroupDelete`                  | `GroupDto`                      |
| `Client_GroupPairJoined`              | `GroupPairFullInfoDto`          |
| `Client_GroupPairLeft`                | `GroupDto`, `UserDto`           |
| `Client_GroupPairChangeUserInfo`      | `GroupPairUserInfoDto`          |

## Error semantics

Server methods throw `HubException` with a short stable code as the message. Known codes:

- `unauthenticated`, `not_found`, `not_paired`, `cannot_pair_self`
- `pair_limit_reached`, `group_limit_owned`, `group_limit_joined`
- `group_not_found`, `banned_from_group`, `group_invites_disabled`, `group_full`
- `already_member`, `bad_password`
- `not_owner`, `not_in_group`, `forbidden`
- `owner_must_transfer_or_delete`, `cannot_remove_self`, `cannot_remove_owner`
- `rate_limited`
- `description_too_long`, `picture_too_large`
- `cannot_block_self`, `blocked`
- `alias_invalid`, `alias_in_use`

Clients should treat unknown error codes as opaque and surface them to the user verbatim.

## Identifier formats

- **UID** — `u-` + 14 chars from a clear-confusables Crockford-base32 alphabet (no `i`, `l`, `o`, `u`). Generated by `Aethernet.Shared.Identity.UidGenerator.NewUid()`.
- **GID** — `g-` + 14 chars, same alphabet.
- **Recovery secret** — `r-` + 32 chars. Shown once.
- **Group password** — 12 chars, no prefix. Rotates on `GroupChangePassword`.
- **File hash** — uppercase SHA-1 hex (40 chars).
