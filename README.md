# RecoveryApp API

Backend for a Digital Detox, Sleep & Recovery mobile app. Clean Architecture, ASP.NET Core Minimal
APIs on .NET 9 (C# 13), PostgreSQL 17 via EF Core + Npgsql, and a local-first two-way sync protocol.

## Quick start

```bash
docker compose up -d
```

```bash
dotnet tool restore && dotnet run --project src/RecoveryApp.Api
```

Then open the API reference at <http://localhost:5080/scalar/v1>.

Start a guest session — no Apple developer account, no registration:

```bash
curl -s -X POST http://localhost:5080/api/v1/auth/anonymous
```

Or sign in with Apple. Development opts into stub verification, so any string works as a test
identity and the same string always lands on the same account:

```bash
curl -s -X POST http://localhost:5080/api/v1/auth/apple -H "Content-Type: application/json" -d '{"identityToken":"dev-alice"}'
```

Full walkthrough, prerequisites and troubleshooting: **[docs/getting-started.md](docs/getting-started.md)**.

## Documentation

| Document | What it covers |
| --- | --- |
| [Getting started](docs/getting-started.md) | Prerequisites, launch steps, verification, migrations, configuration, troubleshooting |
| [Architecture](docs/architecture.md) | Layer boundaries, request lifecycle, cross-cutting concerns, how to add a feature |
| [Sync protocol](docs/sync-protocol.md) | The local-first contract: merge rules, cursors, the client loop, failure modes |
| [API reference](docs/api-reference.md) | Every endpoint with worked `curl` examples |
| [Data model](docs/data-model.md) | Tables, columns, indexes, `jsonb` shapes and why each choice was made |

## Endpoints

| Method | Route | Auth | Notes |
| --- | --- | --- | --- |
| `POST` | `/api/v1/auth/apple` | anonymous | Verifies the identity token against Apple's JWKS; returns JWT + refresh token |
| `POST` | `/api/v1/auth/anonymous` | anonymous | Creates a guest account and returns a session; no registration step |
| `POST` | `/api/v1/auth/link-apple` | bearer | Promotes a guest to registered; merges into an existing account if the Apple ID is taken |
| `POST` | `/api/v1/auth/refresh` | anonymous | Single-use refresh token rotation |
| `GET` | `/api/v1/content/tracks` | bearer | `?category=&includePremium=`; strong `ETag`, `Cache-Control`, `304` on `If-None-Match` |
| `POST` | `/api/v1/sync/push` | bearer | Batch upsert, last-writer-wins, optional `Idempotency-Key` |
| `GET` | `/api/v1/sync/pull` | bearer | `?since=&limit=`; deltas + tombstones, cursor pagination |
| `GET` | `/api/v1/user/settings` | bearer | Read configuration |
| `PUT` | `/api/v1/user/settings` | bearer | Replace configuration |
| `GET` | `/healthz` | anonymous | JSON health report including the Postgres check |
| `GET` | `/openapi/v1.json` | anonymous | OpenAPI 3.0 document |
| `GET` | `/scalar/v1` | anonymous | Scalar API reference UI |

## How it hangs together

Four projects, references pointing inward only:

```
Api ──▶ Infrastructure ──▶ Application ──▶ Domain
```

- **Domain** — entities, enums and invariants. No dependencies at all.
- **Application** — MediatR handlers, FluentValidation validators and the DTO contracts that form the
  OpenAPI schema. Reaches persistence through `IAppDbContext` and the clock through `TimeProvider`,
  so handlers are testable without a database.
- **Infrastructure** — EF Core on Npgsql with snake_case naming, `jsonb` columns, JWT issuance and
  Apple verification.
- **Api** — Minimal API route groups, endpoint filters for validation and idempotency, RFC 9457
  problem details, OpenAPI generation.

Three decisions shape most of the code:

**Client-owned UUIDv7 keys.** Rows are created offline with `Guid.CreateVersion7()`, so a device
needs no server round trip to write, and keys still sort chronologically for the index.

**Last-writer-wins on a client stamp.** `POST /sync/push` overwrites a server row only when the
incoming `updatedAt` is *strictly* newer. Equality loses, which makes a replayed batch a no-op and
retry-on-flaky-network safe by construction. Deletes are tombstones so peers converge.

**One wire spelling per enum.** `SessionType.Focus90` is `focus_90` in JSON, in the `session_type`
column and in a generated TypeScript client — declared once on the enum member and read by both the
JSON converter and the EF value converter. A unit test fails the build if the two ever drift.

## Layout

```
recovery/
├── Directory.Build.props                   # TFM, C# 13, nullable, warnings-as-errors, XML docs
├── Directory.Packages.props                # central package version management
├── RecoveryApp.sln
├── docker-compose.yml                      # PostgreSQL 17 + pgAdmin 4
├── dotnet-tools.json                       # pins dotnet-ef 9.0.19 as a local tool
├── docker/pgadmin/servers.json             # pgAdmin pre-registers the local server
├── docs/                                   # the documents linked above
├── src/
│   ├── RecoveryApp.Domain/                 # entities, enums, invariants — no dependencies
│   │   ├── Common/                         # EnumWireNames, ISyncEntity, SyncMergePolicy, SoundMixConfig
│   │   ├── Entities/                       # User, UserSettings, AudioTrack, UserSession,
│   │   │                                   #   EnergyCheckin, SyncChangelog, RefreshToken,
│   │   │                                   #   IdempotencyRecord
│   │   └── Enums/                          # AudioCategory, SessionType, SyncOperation
│   ├── RecoveryApp.Application/            # MediatR handlers, FluentValidation, DTOs
│   │   ├── Abstractions/                   # IAppDbContext, ICurrentUser, ITokenService, …
│   │   ├── Common/                         # typed exceptions, SyncLimits
│   │   ├── Contracts/                      # request/response DTOs — the OpenAPI schema surface
│   │   ├── Features/                       # Auth, Content, Settings, Sync
│   │   └── DependencyInjection.cs
│   ├── RecoveryApp.Infrastructure/         # EF Core, Npgsql, auth services
│   │   ├── Auth/                           # JwtOptions, TokenService, StubAppleIdentityTokenVerifier
│   │   ├── Persistence/
│   │   │   ├── AppDbContext.cs
│   │   │   ├── AppDbContextFactory.cs      # design-time factory for dotnet ef
│   │   │   ├── DatabaseInitializer.cs      # dev-time migrate + catalogue seed
│   │   │   ├── Configurations/             # table, jsonb, index and check-constraint mapping
│   │   │   ├── Converters/                 # jsonb and enum value converters/comparers
│   │   │   └── Migrations/                 # InitialCreate + AnonymousAccountsAndLoginTracking
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
    └── RecoveryApp.UnitTests/              # xUnit v3 — auth handlers, merge policy, enum names, validators
```

## Common commands

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet ef migrations add <Name> --project src/RecoveryApp.Infrastructure --startup-project src/RecoveryApp.Api --output-dir Persistence/Migrations
```

```bash
dotnet ef database update --project src/RecoveryApp.Infrastructure --startup-project src/RecoveryApp.Api
```

Generate a typed client from the running API:

```bash
npx openapi-typescript http://localhost:5080/openapi/v1.json -o src/api/schema.d.ts
```

## Known scaffold gaps

- Apple identity tokens are verified against Apple's live JWKS by default. Development opts into
  `AppleAuth:UseStubVerification`, which skips the signature check and accepts any string as a test
  identity — convenient locally, never safe anywhere else. The stub has to be asked for explicitly,
  so it cannot reach an environment by being forgotten.
- Guest accounts accumulate. `ix_users_is_anonymous_last_login_at` exists for a reaper that retires
  idle ones, but the job itself is not written.
- `idempotency_records` and `sync_changelog` grow without bound; neither has a retention job yet.
  The `created_at` index on the former exists for one.
- Account merge loads the guest's rows into memory to keep the whole operation in one transaction.
  That is safe while guests stay small and pre-registration; revisit if guest lifetimes grow.
- Migrate-on-startup runs in `Development` only. Deployments should apply migrations as a separate,
  reviewed release step.
