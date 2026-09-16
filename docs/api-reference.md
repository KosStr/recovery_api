# API reference

Base URL in development: `http://localhost:5080`. The live, always-accurate contract is the generated
OpenAPI document — this page is the narrated version.

| Surface | URL |
| --- | --- |
| Scalar reference UI | <http://localhost:5080/scalar/v1> |
| OpenAPI 3.0 document | <http://localhost:5080/openapi/v1.json> |

## Conventions

**Naming.** JSON is camelCase. Enums are strings in their wire spelling — `focus_90`, `power_nap`,
`nsdr`, `digital_detox`, `soundscape`, `breathing` — never integers.

**Nulls are omitted.** The serializer uses `DefaultIgnoreCondition = WhenWritingNull`, so a null
property is absent from the response rather than present as `null`. Treat "absent" and "null" as the
same thing when consuming.

**Timestamps** are ISO 8601 with an explicit offset, e.g. `2026-09-07T10:30:02.4120000+00:00`. The
server always emits UTC. Requests accept any valid offset, including `Z`.

**Times of day** (`targetSleepTime`, `targetWakeTime`) are `HH:mm:ss` wall-clock values with no
timezone — they mean "22:30 wherever the user is".

**Errors** are RFC 9457 problem documents:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "IdentityToken": ["'Identity Token' must not be empty."] },
  "traceId": "0HNOD4398QJ1T:00000001"
}
```

**Authentication** is `Authorization: Bearer <accessToken>` on everything except the two auth routes,
`/healthz`, `/openapi/v1.json` and `/scalar/v1`.

---

## Auth

### `POST /api/v1/auth/anonymous`

Anonymous. Creates a guest account so a first-time user can start recording immediately, with no
registration in front of the product. Returns `201` with the same token pair as a registered sign-in,
and provisions the default settings row.

```bash
curl -s -X POST http://localhost:5080/api/v1/auth/anonymous
```

No request body. The returned `user.isAnonymous` is `true`, and the access token carries an
`is_anonymous` claim so a caller can gate features without a database round trip.

A guest is a real account: it owns data, syncs, and can be upgraded in place later through
`POST /api/v1/auth/link-apple` without losing anything.

### `POST /api/v1/auth/apple`

Anonymous. Exchanges a Sign in with Apple identity token for application tokens, creating the account
and its default settings row on first use. A returning user's `lastLoginAt` moves forward.

The identity token is verified against Apple's live JWKS — signature, `iss`, `aud` and expiry — with
the keys cached and refreshed automatically so Apple's key rollover needs no deployment.

> In `Development`, `AppleAuth:UseStubVerification` is on: the signature is **not** checked and any
> string is accepted as a deterministic test identity. See
> [architecture.md](architecture.md#apple-identity-verification).

```bash
curl -s -X POST http://localhost:5080/api/v1/auth/apple \
  -H "Content-Type: application/json" \
  -d '{"identityToken":"dev-alice"}'
```

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `identityToken` | string | yes | Max 4096 chars |
| `authorizationCode` | string | no | Apple's single-use code, if captured |
| `fullName` | string | no | Max 256 chars; Apple releases it only on first authorization |

```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "kR2vX1s8...",
  "expiresAt": "2026-09-08T12:30:00.0000000+00:00",
  "refreshTokenExpiresAt": "2026-11-07T12:00:00.0000000+00:00",
  "tokenType": "Bearer",
  "user": {
    "id": "0199a1c4-8f00-7c3a-9b21-5f2e7c4a1d33",
    "createdAt": "2026-09-08T12:00:00.0000000+00:00",
    "isNewUser": true,
    "isAnonymous": false,
    "lastLoginAt": "2026-09-08T12:00:00.0000000+00:00"
  }
}
```

`user.email` is present only once Apple has released it — remember, that is the *first* authorization
only, so persist it when you see it. A later sign-in that omits the email never clears the stored one.

`400` malformed body · `401` token expired, wrong audience, wrong issuer or bad signature.

### `POST /api/v1/auth/link-apple`

**Bearer.** Attaches an Apple identity to the calling guest account. This is the upgrade path from
guest to registered.

```bash
curl -s -X POST http://localhost:5080/api/v1/auth/link-apple \
  -H "Authorization: Bearer $GUEST_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"identityToken":"dev-alice"}'
```

Three outcomes, decided by what the Apple identity is already attached to:

| Situation | Result |
| --- | --- |
| Nobody owns that Apple ID | Guest is promoted in place. Same account id, nothing moves. `merged: false` |
| The caller already owns it | Treated as a replay: fresh tokens, `merged: false`. Safe to retry |
| **Another account owns it** | The registered account wins. The guest's rows migrate onto it, the guest is retired, and the returned tokens authenticate the *registered* account. `merged: true` |

```json
{
  "tokens": { "accessToken": "...", "refreshToken": "...", "user": { "id": "0199b2..." } },
  "merged": true,
  "absorbedUserId": "0199a1c4-8f00-7c3a-9b21-5f2e7c4a1d33",
  "migrated": { "sessions": 12, "energyCheckins": 40, "changelogEntries": 52 }
}
```

> **When `merged` is true the caller's identity changed.** `tokens.user.id` is a different account
> from the one the client was just using. Replace the stored credentials *and* the local user id, and
> re-run a full sync from the epoch — the local store is now keyed to the wrong owner.

Merge specifics: sessions, check-ins and changelog entries are re-pointed at the surviving account,
and their `updatedAt` is bumped to now so the registered account's other devices actually pull them
on the next sync. The guest's settings row is **not** carried over — the preserved account keeps its
own configuration. The guest is soft-deleted, not destroyed, and its refresh tokens are revoked.

`400` validation · `401` no session, or the account is gone · `409` the caller is already linked to a
*different* Apple ID (re-pointing a registration is refused, not merged).

### `POST /api/v1/auth/refresh`

Anonymous. Rotates a refresh token into a new pair. The presented token is revoked in the same
transaction, so a stolen token is usable at most once, and reuse is detectable.

```bash
curl -s -X POST http://localhost:5080/api/v1/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"kR2vX1s8..."}'
```

Same response shape as above, with `user.isNewUser` always `false`.

`401` unknown, expired or already-redeemed token, or a deactivated account.

Refresh tokens are stored only as a SHA-256 hash, so a database leak cannot be replayed against this
endpoint.

---

## Content

### `GET /api/v1/content/tracks`

Bearer. Lists the audio catalogue.

| Query | Type | Default | Notes |
| --- | --- | --- | --- |
| `category` | enum | all | `nsdr`, `soundscape` or `breathing` |
| `includePremium` | bool | `true` | `false` hides subscription-only tracks |

```bash
curl -s "http://localhost:5080/api/v1/content/tracks?category=nsdr" -H "Authorization: Bearer $TOKEN"
```

```json
{
  "tracks": [
    {
      "id": "0199a1b0-...",
      "title": "NSDR: 20 Minute Reset",
      "category": "nsdr",
      "durationSeconds": 1200,
      "audioUrl": "https://cdn.recoveryapp.local/audio/nsdr-20-minute-reset.m4a",
      "fileSizeBytes": 18200000,
      "isPremium": false,
      "version": 1
    }
  ],
  "eTag": "\"a3f1c88e2b40d97e5c1a7f30b8e64d21\""
}
```

Response headers:

```
ETag: "a3f1c88e2b40d97e5c1a7f30b8e64d21"
Cache-Control: private, max-age=300
Vary: Authorization
```

**Cache validation.** Send the ETag back and you get `304` with an empty body:

```bash
curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:5080/api/v1/content/tracks" \
  -H "Authorization: Bearer $TOKEN" \
  -H 'If-None-Match: "a3f1c88e2b40d97e5c1a7f30b8e64d21"'
```

The ETag is derived from a single aggregate query over `(count, max(version), max(id))` for the
requested slice, so a `304` is settled **before any track row is materialised**. `version` changes
whenever the underlying audio file changes, which is what tells a client to re-download an asset it
has cached on disk.

`Vary: Authorization` is there because premium visibility differs per caller; without it a shared
cache could serve one user's view to another.

---

## Sync

Both endpoints are covered in depth in [sync-protocol.md](sync-protocol.md). Summarised here.

### `POST /api/v1/sync/push`

Bearer. Optional `Idempotency-Key` header (≤ 128 chars).

```bash
curl -s -X POST http://localhost:5080/api/v1/sync/push \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: 0199a1c9-7e00-73b2-9f10-4c8e2a5b6d90" \
  -d '{
    "sessions": [{
      "id": "0199a1c4-8f00-7c3a-9b21-5f2e7c4a1d33",
      "sessionType": "focus_90",
      "startedAt": "2026-09-07T09:00:00Z",
      "endedAt": "2026-09-07T10:30:00Z",
      "targetDurationSeconds": 5400,
      "actualDurationSeconds": 5400,
      "completedSuccessfully": true,
      "interruptionsCount": 0,
      "metadata": { "app": "ios" },
      "clientCreatedAt": "2026-09-07T09:00:00Z",
      "updatedAt": "2026-09-07T10:30:02Z"
    }],
    "energyCheckins": []
  }'
```

```json
{
  "serverTimestamp": "2026-09-07T11:00:03.4120000+00:00",
  "sessions": { "inserted": 1, "updated": 0, "skipped": 0 },
  "energyCheckins": { "inserted": 0, "updated": 0, "skipped": 0 },
  "conflicts": []
}
```

A replay of the same key returns the identical body plus `Idempotency-Replayed: true`.

`400` validation (≤ 500 rows per type; score 1–5; `endedAt >= startedAt`; `updatedAt` required) ·
`401` · `409` key reused with a different payload.

### `GET /api/v1/sync/pull`

Bearer.

| Query | Type | Default | Notes |
| --- | --- | --- | --- |
| `since` | timestamp | Unix epoch | Cursor from the previous pull |
| `limit` | int | 250 | Per entity type, clamped to 1000 |

```bash
curl -s "http://localhost:5080/api/v1/sync/pull?since=1970-01-01T00:00:00Z&limit=250" \
  -H "Authorization: Bearer $TOKEN"
```

```json
{
  "serverTimestamp": "2026-09-07T11:05:00.0000000+00:00",
  "nextCursor": "2026-09-07T11:04:59.0000000+00:00",
  "hasMore": false,
  "sessions": [],
  "energyCheckins": [],
  "settings": { "userId": "0199a1c4-...", "updatedAt": "..." }
}
```

Keep calling with `nextCursor` while `hasMore` is `true`. Tombstoned rows appear with `deletedAt`
set — delete them locally.

---

## User

### `GET /api/v1/user/settings`

Bearer.

```bash
curl -s http://localhost:5080/api/v1/user/settings -H "Authorization: Bearer $TOKEN"
```

```json
{
  "userId": "0199a1c4-8f00-7c3a-9b21-5f2e7c4a1d33",
  "targetSleepTime": "22:30:00",
  "targetWakeTime": "06:30:00",
  "caffeineCutoffHours": 8,
  "digitalSunsetMinutes": 60,
  "hapticEnabled": true,
  "soundMixConfig": { "masterVolume": 1, "layers": [], "fadeOutSeconds": 10 },
  "updatedAt": "2026-09-08T12:00:00.0000000+00:00"
}
```

`404` if the settings row is missing — it is created with the account, so this should not happen in
practice.

### `PUT /api/v1/user/settings`

Bearer. A **full replacement**, not a patch: send the whole document.

```bash
curl -s -X PUT http://localhost:5080/api/v1/user/settings \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "targetSleepTime": "23:00:00",
    "targetWakeTime": "07:00:00",
    "caffeineCutoffHours": 10,
    "digitalSunsetMinutes": 90,
    "hapticEnabled": true,
    "soundMixConfig": {
      "masterVolume": 0.8,
      "fadeOutSeconds": 15,
      "layers": [
        { "soundId": "rain_heavy", "volume": 0.6, "enabled": true },
        { "soundId": "brown_noise", "volume": 0.3, "enabled": false }
      ]
    }
  }'
```

Returns the saved `UserSettingsResponse` with a fresh server `updatedAt`, which is also what makes
the row appear in the next `/sync/pull` on the user's other devices.

| Field | Rule |
| --- | --- |
| `caffeineCutoffHours` | 0–24 |
| `digitalSunsetMinutes` | 0–480 |
| `soundMixConfig.masterVolume` | 0.0–1.0 |
| `soundMixConfig.fadeOutSeconds` | 0–300 |
| `soundMixConfig.layers` | ≤ 16 entries |
| `layers[].soundId` | required, ≤ 64 chars |
| `layers[].volume` | 0.0–1.0 |

`soundMixConfig` is stored as a `jsonb` column, so adding a field to the mix needs no migration.

---

## Diagnostics

### `GET /healthz`

Anonymous. Returns `200` when healthy, `503` when any check fails.

```json
{
  "status": "Healthy",
  "totalDurationMs": 12.4,
  "checks": [{ "name": "postgres", "status": "Healthy", "durationMs": 11.9 }]
}
```

The `postgres` check is an EF Core `DbContext` connectivity probe tagged `ready`, which makes this
endpoint suitable as a readiness probe.

---

## Generating a typed client

The OpenAPI document carries the C# XML documentation as schema and property descriptions, so a
generated client keeps the prose:

```bash
npx openapi-typescript http://localhost:5080/openapi/v1.json -o src/api/schema.d.ts
```

Or check in a static copy for CI:

```bash
curl -s http://localhost:5080/openapi/v1.json -o openapi.json
```
