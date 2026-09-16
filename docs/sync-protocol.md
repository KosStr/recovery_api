# The sync protocol

The mobile client is **local-first**: it writes to its own store and renders from it, then reconciles
with the server in the background. The user never waits on the network, and the app is fully
functional on a plane.

That design only works if the server agrees to a specific set of rules. This document is the
contract.

## The four rules

1. **The client owns the identifier.** Rows are created on device with `Guid.CreateVersion7()` and
   keep that id forever. The server never mints one for a synced row.
2. **The client owns the clock.** `updatedAt` is set by the device on every local edit. The server
   stores it verbatim and uses it as the merge clock and the pull cursor.
3. **Newer strictly wins.** An incoming revision overwrites the stored row only when its `updatedAt`
   is *strictly* greater. Equal loses.
4. **Deletes are tombstones.** `deletedAt` is set; the row is never physically removed, so other
   devices can learn about the deletion.

Rule 3 is the important one, and it is a single function in the Domain layer:

```csharp
public static bool ShouldApply(DateTimeOffset incomingUpdatedAt, DateTimeOffset storedUpdatedAt) =>
    incomingUpdatedAt > storedUpdatedAt;
```

Because equality loses, **re-sending a batch changes nothing**. Push is naturally idempotent, before
any `Idempotency-Key` header enters the picture. That is what makes retry-on-flaky-network safe.

## Which entities sync

| Entity | Direction | Notes |
| --- | --- | --- |
| `UserSession` | push + pull | Client id, tombstoned |
| `EnergyCheckin` | push + pull | Client id, tombstoned |
| `UserSettings` | pull only | Written through `PUT /api/v1/user/settings`; server sets `updatedAt` |
| `AudioTrack` | neither | Global catalogue, cached over HTTP with an ETag instead |

Settings are deliberately not part of push. There is one row per user, it is small, and a dedicated
`PUT` gives a clearer conflict story than a batch merge. It still rides along on **pull**, so a
change made on the phone reaches the tablet.

## Push

```
POST /api/v1/sync/push
Authorization: Bearer <accessToken>
Idempotency-Key: <optional client-generated key>
```

```json
{
  "sessions": [
    {
      "id": "0199a1c4-8f00-7c3a-9b21-5f2e7c4a1d33",
      "sessionType": "focus_90",
      "startedAt": "2026-09-07T09:00:00Z",
      "endedAt": "2026-09-07T10:30:00Z",
      "targetDurationSeconds": 5400,
      "actualDurationSeconds": 5400,
      "completedSuccessfully": true,
      "interruptionsCount": 0,
      "metadata": { "app": "ios", "buildNumber": 412 },
      "clientCreatedAt": "2026-09-07T09:00:00Z",
      "updatedAt": "2026-09-07T10:30:02Z",
      "deletedAt": null
    }
  ],
  "energyCheckins": [
    {
      "id": "0199a1c5-1200-7f81-a0c4-2b9d6e0f5a17",
      "score": 4,
      "context": "after a walk",
      "tags": ["outdoors", "post_nap"],
      "recordedAt": "2026-09-07T11:00:00Z",
      "updatedAt": "2026-09-07T11:00:00Z",
      "deletedAt": null
    }
  ]
}
```

Both arrays are optional and may be `null` or empty. At most **500 rows per entity type** per call.

### What the server does

For each entity type, in one round trip:

1. Collapse duplicate ids in the batch, keeping the newest revision of each. A retrying client can
   legitimately send the same id twice; the server does not need to care.
2. Load every existing row for those ids in a single `WHERE user_id = ? AND id = ANY(?)` query.
3. For each incoming row:
   - not on the server → **insert**,
   - on the server and strictly newer → **update**,
   - otherwise → **skip**, and report a conflict.
4. Append one `sync_changelog` entry per applied mutation.
5. Commit once.

### Response

```json
{
  "serverTimestamp": "2026-09-07T11:00:03.412Z",
  "sessions": { "inserted": 1, "updated": 0, "skipped": 0 },
  "energyCheckins": { "inserted": 1, "updated": 0, "skipped": 0 },
  "conflicts": []
}
```

A conflict is not an error. It tells the client the server kept a newer revision, and hands over both
stamps so the client can decide what to do:

```json
{
  "conflicts": [
    {
      "entityName": "UserSession",
      "entityId": "0199a1c4-8f00-7c3a-9b21-5f2e7c4a1d33",
      "serverUpdatedAt": "2026-09-07T10:31:00Z",
      "clientUpdatedAt": "2026-09-07T10:30:02Z"
    }
  ]
}
```

The usual client response is to do nothing: the next pull delivers the winning revision anyway.

### Idempotency-Key

Optional, at most 128 characters, scoped per user. On first use the response body and status are
stored in `idempotency_records`. A replay of the same key:

- with the same request → returns the stored response, adds `Idempotency-Replayed: true`, and never
  re-enters the handler,
- with a *different* request or against a different path → `409`.

So push gives you two layers: convergence makes re-applying harmless, and the key makes the *reported
counters and conflicts* identical on every retry too. Use it when the client's UI reacts to those
numbers.

## Pull

```
GET /api/v1/sync/pull?since=2026-09-07T11:00:03.412Z&limit=250
Authorization: Bearer <accessToken>
```

| Parameter | Default | Notes |
| --- | --- | --- |
| `since` | Unix epoch | Cursor from the previous call. Omit to bootstrap a device. |
| `limit` | 250 | Rows per entity type. Clamped to 1000. |

Returns every row for the caller whose `updatedAt` is strictly greater than `since`, **tombstones
included**, ordered by `(updatedAt, id)`:

```json
{
  "serverTimestamp": "2026-09-07T11:05:00Z",
  "nextCursor": "2026-09-07T11:04:59Z",
  "hasMore": false,
  "sessions": [ ... ],
  "energyCheckins": [ ... ],
  "settings": { "userId": "...", "targetSleepTime": "22:30:00", "updatedAt": "..." }
}
```

`settings` is `null` unless the settings row changed after the cursor.

### The cursor

Two cases, and the difference matters:

- **Page was truncated** (`hasMore: true`) — the cursor is the `updatedAt` of the last row returned
  by whichever stream still has rows waiting. It cannot run past a row the client has not seen.
- **Everything was drained** (`hasMore: false`) — the cursor is the server clock **minus one second**
  (`SyncLimits.CursorOverlap`).

That one-second rollback is not paranoia. A transaction that started before the read but commits
after it writes a row with an earlier `updatedAt` than the cursor would otherwise have been. Without
the overlap, the next pull would skip that row permanently. With it, the client re-sees a small
window of rows it may already have — which is harmless, because applying a row it already holds is a
no-op under the same last-writer-wins rule.

**Do not** substitute your own clock for `nextCursor`. Device and server clocks drift, and the whole
protocol is anchored on server-returned values.

## The client loop

```
on app start, on foreground, and every N minutes:

  1. collect local rows where dirty = true
  2. POST /sync/push in chunks of <= 500 per entity type
       └─ on success: mark those rows clean
       └─ on network failure: keep them dirty, retry later (safe — push is idempotent)

  3. loop:
       GET /sync/pull?since=<storedCursor>&limit=250
       apply each returned row locally, last-writer-wins against the local copy
       delete locally where deletedAt is set
       storedCursor = response.nextCursor
       while response.hasMore
```

Push before pull. If you pull first you may overwrite a local edit that has not been uploaded yet.

Applying a pulled row locally uses the same comparison the server does: take it only if its
`updatedAt` is newer than the local copy's. That keeps a local edit made *during* the sync from being
clobbered.

## Worked example: two devices, one session

| # | Event | iPhone | iPad | Server |
| --- | --- | --- | --- | --- |
| 1 | iPhone records a session, offline | `id=A, updated=10:30` | — | — |
| 2 | iPhone pushes | clean | — | `A, updated=10:30` (inserted) |
| 3 | iPad pulls | | `A, updated=10:30` | |
| 4 | iPad edits the note, offline | | `A, updated=10:45` dirty | |
| 5 | iPhone edits too, offline | `A, updated=10:40` dirty | | |
| 6 | iPhone pushes | clean | | `A, updated=10:40` (updated) |
| 7 | iPad pushes | | clean | `A, updated=10:45` (updated) |
| 8 | iPhone pulls | `A, updated=10:45` | | |

Both devices converge on the iPad's 10:45 revision — the later edit. If step 7 had arrived first,
step 6 would have been reported as a conflict with `serverUpdatedAt = 10:45`, and the iPhone would
still land on 10:45 at step 8. Order of arrival does not change the outcome; only the stamps do.

## Deletion

A delete is an ordinary revision that happens to set `deletedAt`:

```json
{ "id": "0199a1c4-...", "updatedAt": "2026-09-07T12:00:00Z", "deletedAt": "2026-09-07T12:00:00Z", ... }
```

It is subject to the same rule as any other revision — a tombstone stamped `12:00` loses to an edit
stamped `12:01`. Tombstones are returned by pull so peers can remove their local copy, and they stay
in the table indefinitely; a retention job that hard-deletes rows tombstoned long enough for every
device to have synced is future work.

## The changelog

Every applied mutation appends to `sync_changelog` (`insert` / `update` / `delete`, with the user,
entity name, entity id and server timestamp). The last-writer-wins tables only hold the current
state, so this is the only place that can answer "what happened to this row, and when" — useful for
support and for reconstructing a device's history after a bug report.

It is append-only and never read by the sync endpoints themselves.

## Failure modes and what the client should do

| Status | Meaning | Client action |
| --- | --- | --- |
| `400` | A row failed validation. The `errors` object names the index and field. | Fix or quarantine the row; do not blind-retry. |
| `401` | Access token expired or invalid. | Refresh, then retry the batch. |
| `409` | `Idempotency-Key` reused with a different payload. | Bug — generate a fresh key per distinct batch. |
| `499` / timeout / offline | Unknown whether the server applied it. | Retry the identical batch. Convergence makes this safe. |
| `5xx` | Server fault. | Exponential backoff, then retry the identical batch. |

The "unknown outcome" case is the one that motivates the whole design. A local-first client cannot
tell a lost request from a lost response, so the protocol is built so that it never has to.

## Limits

| Limit | Value | Where |
| --- | --- | --- |
| Max rows per entity type in a push | 500 | `SyncLimits.MaxPushBatchSize` |
| Default pull page size | 250 | `SyncLimits.DefaultPullPageSize` |
| Max pull page size | 1000 | `SyncLimits.MaxPullPageSize` |
| Cursor overlap | 1 second | `SyncLimits.CursorOverlap` |
| Max tags per check-in | 32 | `SyncPushRequestValidator` |
| Max `context` length | 1000 chars | `SyncPushRequestValidator` |
| Max session duration | 86 400 s | `SyncPushRequestValidator` |
| Max `Idempotency-Key` length | 128 chars | `IdempotencyFilter` |

## Indexes that make it work

Every sync query filters by owner and revision stamp, in that order:

```sql
CREATE INDEX ix_user_sessions_user_id_updated_at   ON user_sessions   (user_id, updated_at);
CREATE INDEX ix_energy_checkins_user_id_updated_at ON energy_checkins (user_id, updated_at);
CREATE INDEX ix_user_settings_updated_at           ON user_settings   (updated_at);
```

Combined with UUIDv7 primary keys — which keep inserts at the right edge of the B-tree rather than
scattered across it — a pull stays an index range scan as the table grows.
