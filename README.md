# RecoveryApp API

Backend for a Digital Detox, Sleep & Recovery mobile app. Clean Architecture, ASP.NET Core Minimal
APIs on .NET 9 (C# 13), PostgreSQL 17 via EF Core + Npgsql, and a local-first two-way sync protocol.

## Layout

```
recovery/
├── Directory.Build.props                   # TFM, C# 13, nullable, warnings-as-errors, XML docs
├── Directory.Packages.props                # central package version management
├── RecoveryApp.sln
├── docker-compose.yml                      # PostgreSQL 17 + pgAdmin 4
├── dotnet-tools.json                       # pins dotnet-ef 9.0.19 as a local tool
├── docker/pgadmin/servers.json             # pgAdmin pre-registers the local server
├── src/
│   ├── RecoveryApp.Domain/                 # entities, enums, invariants — no dependencies
│   │   ├── Common/
│   │   │   ├── EnumWireNames.cs            # one spelling for JSON, Postgres and TypeScript
│   │   │   ├── ISyncEntity.cs
│   │   │   ├── SoundMixConfig.cs           # jsonb value object
│   │   │   └── SyncMergePolicy.cs          # last-writer-wins rule
│   │   ├── Entities/
│   │   │   ├── AudioTrack.cs
│   │   │   ├── EnergyCheckin.cs
│   │   │   ├── IdempotencyRecord.cs
│   │   │   ├── RefreshToken.cs
│   │   │   ├── SyncChangelog.cs
│   │   │   ├── User.cs
│   │   │   ├── UserSession.cs
│   │   │   └── UserSettings.cs
│   │   └── Enums/                          # AudioCategory, SessionType, SyncOperation
│   ├── RecoveryApp.Application/            # MediatR handlers, FluentValidation, DTOs
│   │   ├── Abstractions/                   # IAppDbContext, ICurrentUser, ITokenService, …
│   │   ├── Common/                         # typed exceptions, SyncLimits
│   │   ├── Contracts/                      # request/response DTOs — the OpenAPI schema surface
│   │   ├── Features/
│   │   │   ├── Auth/                       # SignInWithApple, RefreshTokens
│   │   │   ├── Content/                    # GetAudioTracks (+ ETag computation)
│   │   │   ├── Settings/                   # Get/UpdateUserSettings
│   │   │   └── Sync/                       # PushSyncChanges, PullSyncChanges
│   │   └── DependencyInjection.cs
│   ├── RecoveryApp.Infrastructure/         # EF Core, Npgsql, auth services
│   │   ├── Auth/                           # JwtOptions, TokenService, StubAppleIdentityTokenVerifier
│   │   ├── Persistence/
│   │   │   ├── AppDbContext.cs
│   │   │   ├── AppDbContextFactory.cs      # design-time factory for dotnet ef
│   │   │   ├── DatabaseInitializer.cs      # dev-time migrate + catalogue seed
│   │   │   ├── Configurations/             # table, jsonb, index and check-constraint mapping
│   │   │   ├── Converters/                 # jsonb and enum value converters/comparers
│   │   │   └── Migrations/                 # 20260907203943_InitialCreate
│   │   └── DependencyInjection.cs
│   └── RecoveryApp.Api/                    # Minimal APIs, filters, OpenAPI, health
│       ├── Endpoints/                      # Auth, Content, Sync, User route groups
│       ├── Extensions/                     # JSON, JWT bearer, OpenAPI, health registration
│       ├── Filters/                        # ValidationFilter<T>, IdempotencyFilter
│       ├── Middleware/                     # GlobalExceptionHandler → RFC 9457 problem details
│       ├── OpenApi/                        # XML-doc + bearer security transformers
│       ├── Security/                       # HttpContextCurrentUser
│       ├── Program.cs
│       ├── appsettings.json
│       └── appsettings.Development.json
└── tests/
    └── RecoveryApp.UnitTests/              # xUnit v3 — merge policy, enum wire names, validators
```

## Endpoints

| Method | Route | Auth | Notes |
| --- | --- | --- | --- |
| `POST` | `/api/v1/auth/apple` | anonymous | Stubbed identity-token verification; returns JWT + refresh token |
| `POST` | `/api/v1/auth/refresh` | anonymous | Single-use refresh token rotation |
| `GET` | `/api/v1/content/tracks` | bearer | `?category=&includePremium=`; strong `ETag`, `Cache-Control`, `304` on `If-None-Match` |
| `POST` | `/api/v1/sync/push` | bearer | Batch upsert, last-writer-wins, optional `Idempotency-Key` |
| `GET` | `/api/v1/sync/pull` | bearer | `?since=&limit=`; deltas + tombstones, cursor pagination |
| `GET` | `/api/v1/user/settings` | bearer | Read configuration |
| `PUT` | `/api/v1/user/settings` | bearer | Replace configuration |
| `GET` | `/healthz` | anonymous | JSON health report including the Postgres check |
| `GET` | `/openapi/v1.json` | anonymous | OpenAPI 3.0 document |
| `GET` | `/scalar/v1` | anonymous | Scalar API reference UI |

## Running it

Start the database and pgAdmin:

```bash
docker compose up -d
```

pgAdmin is on <http://localhost:5050> (`dev@recoveryapp.local` / `recoveryapp`), Postgres on
`localhost:5432` (`recoveryapp` / `recoveryapp` / db `recoveryapp`).

Restore the local `dotnet-ef` tool once, then run the API:

```bash
dotnet tool restore
```

```bash
dotnet run --project src/RecoveryApp.Api
```

In `Development` the host applies pending migrations and seeds the audio catalogue on startup. If
Postgres is not up yet it logs a warning and keeps serving, so the API reference stays browsable and
`/healthz` reports the outage rather than the process dying at boot.

Then open <http://localhost:5080/scalar/v1>.

## Database migrations

```bash
dotnet ef migrations add <Name> --project src/RecoveryApp.Infrastructure --startup-project src/RecoveryApp.Api --output-dir Persistence/Migrations
```

```bash
dotnet ef database update --project src/RecoveryApp.Infrastructure --startup-project src/RecoveryApp.Api
```

For a deployment, generate a reviewable idempotent script instead of migrating from the app:

```bash
dotnet ef migrations script --idempotent --project src/RecoveryApp.Infrastructure --startup-project src/RecoveryApp.Api -o schema.sql
```

`AppDbContextFactory` supplies the design-time connection string, so the commands work without the
API's configuration; override it with the `ConnectionStrings__Postgres` environment variable.

## Tests

```bash
dotnet test
```

```bash
dotnet test --logger "console;verbosity=detailed"
```

## Generating a TypeScript client

The OpenAPI document carries the XML documentation as schema and property descriptions, so a
generated client keeps the prose:

```bash
npx openapi-typescript http://localhost:5080/openapi/v1.json -o src/api/schema.d.ts
```

## Sync protocol

The client owns the identifiers and the clock:

- Rows are created offline with `Guid.CreateVersion7()`, so keys are unique, time-ordered and index
  friendly without a server round trip.
- `updatedAt` is the client revision stamp. `POST /sync/push` overwrites a server row only when the
  incoming stamp is **strictly** newer (`SyncMergePolicy`), which makes a replayed batch a no-op.
  Rows that lose come back in `conflicts` with the revision the server kept.
- Deletes are tombstones (`deletedAt`), never `DELETE`, so other devices can converge.
- `GET /sync/pull` pages by `(updatedAt, id)` and returns `nextCursor` plus `hasMore`. When the
  client has drained everything the cursor is pulled back one second so rows committed by
  transactions still open during the read are not skipped.
- Every applied mutation is appended to `sync_changelog`.

## Configuration

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:Postgres` | Npgsql connection string |
| `Jwt:SigningKey` | HMAC-SHA256 key, ≥ 32 bytes — **required**, validated at startup |
| `Jwt:Issuer`, `Jwt:Audience` | Access token claims and validation parameters |
| `Jwt:AccessTokenLifetime`, `Jwt:RefreshTokenLifetime` | Token lifetimes |
| `AppleAuth:ClientId`, `AppleAuth:Issuer` | Expected `aud` and `iss` of the Apple identity token |
| `AppleAuth:UseStubVerification` | Guards the development stub verifier |

The development signing key lives in `appsettings.Development.json`. Supply a real one through user
secrets or the environment before deploying:

```bash
dotnet user-secrets set "Jwt:SigningKey" "<32+ byte random value>" --project src/RecoveryApp.Api
```

## Known scaffold gaps

- `StubAppleIdentityTokenVerifier` decodes the identity token but does **not** verify its signature.
  Replace it with a JWKS-backed verifier against `https://appleid.apple.com/auth/keys` before any
  deployment; `AppleAuth:UseStubVerification` marks the seam.
- `idempotency_records` has no retention job yet; the `created_at` index is there for one.
