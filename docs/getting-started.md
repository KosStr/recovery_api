# Getting started

Everything you need to go from a fresh clone to a running API with a real database and a working
access token.

## 1. Prerequisites

| Tool | Version | Why |
| --- | --- | --- |
| .NET SDK | 9.0 or newer | The solution targets `net9.0` / C# 13 |
| Docker Desktop | any current | Runs PostgreSQL 17 and pgAdmin |

Check the SDK:

```bash
dotnet --info
```

### A note on .NET 10

`Directory.Build.props` sets `<RollForward>LatestMajor</RollForward>`. That means the projects are
**compiled** against the .NET 9 targeting pack but are allowed to **execute** on a newer major
runtime. So a machine with only the .NET 10 SDK and runtime installed builds and runs this solution
without installing .NET 9 — which is exactly how it was developed and verified.

If you do have the .NET 9 runtime installed, nothing changes: roll-forward only kicks in when no
matching major version is present.

## 2. Start the database

```bash
docker compose up -d
```

This brings up two containers:

| Service | Address | Credentials |
| --- | --- | --- |
| PostgreSQL 17 | `localhost:5432` | user `recoveryapp` / password `recoveryapp` / db `recoveryapp` |
| pgAdmin 4 | <http://localhost:5050> | `dev@recoveryapp.local` / `recoveryapp` |

pgAdmin pre-registers the server from `docker/pgadmin/servers.json`, so the `RecoveryApp (local)`
connection is already in the tree when you open it. Postgres data survives restarts in the
`postgres-data` volume.

Wait for the healthcheck to pass before moving on:

```bash
docker compose ps
```

> **Port 5432 already in use?** See [Troubleshooting](#troubleshooting) — this is the single most
> common thing that goes wrong, and the symptom is misleading.

## 3. Restore the local tools

The `dotnet-ef` CLI is pinned as a local tool in `dotnet-tools.json` so everyone runs the same
version as the EF Core packages:

```bash
dotnet tool restore
```

## 4. Run the API

```bash
dotnet run --project src/RecoveryApp.Api
```

The `http` launch profile binds <http://localhost:5080> and opens the Scalar API reference at
<http://localhost:5080/scalar/v1>.

On startup in the `Development` environment the host:

1. applies any pending EF Core migrations, then
2. seeds the audio catalogue with eight starter tracks if `audio_tracks` is empty.

If Postgres is not reachable it **logs a warning and keeps serving**. That is deliberate: the API
reference stays browsable while the database is still coming up, and `/healthz` reports the outage
instead of the process dying at boot. Every data endpoint will fail until the database is back.

> Migrate-on-startup is a development convenience only. For anything deployed, see
> [Applying migrations deliberately](#applying-migrations-deliberately).

## 5. Verify it works

```bash
curl -s http://localhost:5080/healthz
```

A healthy response — this is the check that proves the database connection actually works:

```json
{"status":"Healthy","totalDurationMs":12.4,"checks":[{"name":"postgres","status":"Healthy","durationMs":11.9,"description":null}]}
```

The OpenAPI document and the reference UI:

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5080/openapi/v1.json
```

Protected routes reject anonymous callers with `401`:

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5080/api/v1/content/tracks
```

## 6. Get a token and call a protected endpoint

The fastest path needs no Apple developer account at all — start a guest session:

```bash
curl -s -X POST http://localhost:5080/api/v1/auth/anonymous
```

That returns the same token pair as a registered sign-in, against an account with
`isAnonymous: true`. You can record sessions and sync immediately, and upgrade the account later with
`POST /api/v1/auth/link-apple` without losing any data.

To exercise the Apple path instead: in `Development`, `AppleAuth:UseStubVerification` is on, so
verification is **stubbed**. The stub decodes real Apple JWTs, but it also accepts any arbitrary
string and folds it into a deterministic pseudo user id — so you can sign in from a terminal or the
simulator long before the Apple developer account is wired up. Passing the same string always lands
you on the same account.

Outside `Development` the default flips: tokens are verified against Apple's live JWKS, and a string
like `dev-alice` is rejected with `401`.

```bash
curl -s -X POST http://localhost:5080/api/v1/auth/apple -H "Content-Type: application/json" -d '{"identityToken":"dev-alice"}'
```

The response carries the access token, the refresh token and the account:

```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "refreshToken": "kR2v...",
  "expiresAt": "2026-09-08T12:30:00+00:00",
  "refreshTokenExpiresAt": "2026-11-07T12:00:00+00:00",
  "tokenType": "Bearer",
  "user": { "id": "01997c...", "createdAt": "...", "isNewUser": true }
}
```

(`user.email` is absent rather than `null` — the serializer omits null properties, and Apple only
releases an email on the very first authorization.)

Capture it and call the catalogue:

```bash
TOKEN=$(curl -s -X POST http://localhost:5080/api/v1/auth/apple -H "Content-Type: application/json" -d '{"identityToken":"dev-alice"}' | sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p')
```

```bash
curl -s http://localhost:5080/api/v1/content/tracks -H "Authorization: Bearer $TOKEN"
```

Access tokens live 15 minutes, refresh tokens 60 days. See [api-reference.md](api-reference.md) for
every endpoint with worked examples.

## Running the tests

```bash
dotnet test
```

The unit tests cover the auth handlers (Apple sign-in, guest accounts, Apple linking and the merge
path), the sync merge policy, the enum wire-name contract and the request validators. Handler tests
run against the EF in-memory provider, so the suite needs no database and no network.

## Applying migrations deliberately

Migrate-on-startup is fine on a laptop and wrong everywhere else. For a deployment, generate a
reviewable script and apply it as its own gated release step:

```bash
dotnet ef migrations script --idempotent --project src/RecoveryApp.Infrastructure --startup-project src/RecoveryApp.Api -o schema.sql
```

Or apply directly against a target database:

```bash
dotnet ef database update --project src/RecoveryApp.Infrastructure --startup-project src/RecoveryApp.Api
```

Add a migration after changing an entity or a configuration:

```bash
dotnet ef migrations add <Name> --project src/RecoveryApp.Infrastructure --startup-project src/RecoveryApp.Api --output-dir Persistence/Migrations
```

`AppDbContextFactory` supplies the design-time connection string, so these commands work without the
API's configuration. Override it with the `ConnectionStrings__Postgres` environment variable:

```bash
ConnectionStrings__Postgres="Host=db.internal;Port=5432;Database=recoveryapp;Username=deploy;Password=***" dotnet ef database update --project src/RecoveryApp.Infrastructure --startup-project src/RecoveryApp.Api
```

## Configuration

| Key | Default | Purpose |
| --- | --- | --- |
| `ConnectionStrings:Postgres` | local docker compose | Npgsql connection string |
| `Jwt:SigningKey` | empty; dev value in `appsettings.Development.json` | HMAC-SHA256 key, **min 32 bytes**, validated at startup |
| `Jwt:Issuer` | `https://api.recoveryapp.local` | `iss` claim, also validated on inbound tokens |
| `Jwt:Audience` | `recoveryapp-mobile` | `aud` claim, also validated on inbound tokens |
| `Jwt:AccessTokenLifetime` | `00:15:00` | Access token lifetime |
| `Jwt:RefreshTokenLifetime` | `60.00:00:00` | Refresh token lifetime |
| `AppleAuth:ClientId` | `com.recoveryapp.mobile` | Expected `aud` of the Apple identity token |
| `AppleAuth:Issuer` | `https://appleid.apple.com` | Expected `iss` of the Apple identity token |
| `AppleAuth:MetadataAddress` | Apple's OIDC discovery URL | Where the signing keys are fetched from |
| `AppleAuth:KeyCacheDuration` | `12:00:00` | How long fetched signing keys are reused before a refresh |
| `AppleAuth:UseStubVerification` | `false`; `true` in `appsettings.Development.json` | Skips signature verification entirely. Development only |

Before deploying, supply a real signing key out of band:

```bash
dotnet user-secrets set "Jwt:SigningKey" "<32+ byte random value>" --project src/RecoveryApp.Api
```

Or through the environment, using the double-underscore convention:

```bash
export Jwt__SigningKey="<32+ byte random value>"
```

## Troubleshooting

### `password authentication failed for user "recoveryapp"`

Something else is already listening on port 5432 — usually a Postgres installed directly on the
host. Your connection is reaching *that* server, which has no `recoveryapp` role, so the error looks
like a credentials problem when it is really a port collision.

Confirm what owns the port:

```bash
docker compose ps
```

Then either stop the host service, or move the container to a free port in `docker-compose.yml`:

```yaml
    ports:
      - "5433:5432"
```

and point the connection string at it:

```bash
export ConnectionStrings__Postgres="Host=localhost;Port=5433;Database=recoveryapp;Username=recoveryapp;Password=recoveryapp"
```

### `/healthz` returns 503 with `"status":"Unhealthy"`

The API is up but cannot reach Postgres. The `checks[].name: "postgres"` entry is the one that
failed. Start the database and the next request recovers — no restart needed:

```bash
docker compose up -d postgres
```

Note that startup migrations only run once, at boot. If the database was down when the API started,
restart the API so the schema gets created.

### Startup fails with a `Jwt:SigningKey` validation error

`JwtOptions` is validated on start, and the key must be at least 32 characters. The development key
ships in `appsettings.Development.json`; any other environment must supply its own. This is a
deliberate failure — the app refuses to run with no signing key rather than minting unverifiable
tokens.

### `401 "The identity token is not valid."` on `/auth/apple`

You are running with real verification — the default outside `Development`. Apple's JWKS was
reachable and the token simply did not pass, so a placeholder string like `dev-alice` will not work
here. Either send a genuine identity token, or set `AppleAuth:UseStubVerification` to `true` for a
local environment.

If the failure is `500` rather than `401`, Apple's key endpoint could not be reached at all; that is
an upstream or network problem, not a bad credential, and the two are kept distinct on purpose.

### `dotnet ef` is not recognised

```bash
dotnet tool restore
```

### `401` on every protected call

Check that you are sending `Authorization: Bearer <accessToken>` and not the refresh token, and that
the access token has not expired (15 minutes). `POST /api/v1/auth/refresh` exchanges the refresh
token for a new pair.
