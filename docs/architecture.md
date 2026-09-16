# Architecture

How the four projects fit together, what each one is allowed to know, and where new code goes.

## The dependency rule

```
                 ┌─────────────────────┐
                 │  RecoveryApp.Api    │  Minimal APIs, filters, OpenAPI, auth wiring
                 └──────────┬──────────┘
                            │
                 ┌──────────▼──────────┐
                 │ RecoveryApp.        │  EF Core, Npgsql, JWT issuance, Apple verification
                 │ Infrastructure      │
                 └──────────┬──────────┘
                            │
                 ┌──────────▼──────────┐
                 │ RecoveryApp.        │  MediatR handlers, validators, DTO contracts
                 │ Application         │
                 └──────────┬──────────┘
                            │
                 ┌──────────▼──────────┐
                 │ RecoveryApp.Domain  │  entities, enums, invariants — zero dependencies
                 └─────────────────────┘
```

References point inward only. The practical consequences:

- **Domain** references nothing — not EF Core, not ASP.NET. It holds the entities, the enums and the
  rules that are true regardless of how the app is delivered (`SyncMergePolicy`, the 1–5 score
  bounds, the wire spelling of every enum).
- **Application** owns the use cases. It talks to persistence through `IAppDbContext` and to the
  clock through `TimeProvider`, so a handler can be exercised without a database or a real time
  source. It never sees `HttpContext`.
- **Infrastructure** implements those abstractions. It is the only project that knows the schema is
  Postgres, that tokens are HMAC-signed, and that snake_case is the naming convention.
- **API** is transport. It binds HTTP to `ISender`, turns exceptions into problem documents, and
  owns nothing the other layers need.

Inversion happens through the `Abstractions` folder in Application:

| Abstraction | Implemented by | Purpose |
| --- | --- | --- |
| `IAppDbContext` | `AppDbContext` (Infrastructure) | The only persistence surface handlers may touch |
| `ICurrentUser` | `HttpContextCurrentUser` (Api) | Caller identity, read from the validated bearer token |
| `ITokenService` | `TokenService` (Infrastructure) | Access token minting, refresh token hashing |
| `IAppleIdentityTokenVerifier` | `AppleJwksIdentityTokenVerifier`, or `StubAppleIdentityTokenVerifier` in development (Infrastructure) | Apple identity token verification |

`ICurrentUser` is implemented in the API rather than Infrastructure on purpose: "who is calling" is a
transport concern, and the implementation needs `IHttpContextAccessor`.

## Request lifecycle

A `POST /api/v1/sync/push` is the busiest path in the app, so it makes the best worked example:

```
HTTP request
  │
  ├─ UseExceptionHandler ─────────── GlobalExceptionHandler catches anything below
  │
  ├─ Authentication ──────────────── JWT bearer validates iss/aud/signature/lifetime
  ├─ Authorization ───────────────── route group calls RequireAuthorization()
  │
  ├─ Model binding ───────────────── SyncPushRequest deserialized from the body
  │
  ├─ ValidationFilter<SyncPushRequest>
  │     └─ resolves IValidator<T>, returns 400 + field errors if invalid
  │
  ├─ IdempotencyFilter
  │     └─ if Idempotency-Key was seen before → replays the stored response, stops here
  │
  ├─ Endpoint delegate ───────────── wraps the DTO in PushSyncChangesCommand
  │     └─ ISender.Send(...)
  │
  ├─ PushSyncChangesHandler ──────── ICurrentUser → user id, TimeProvider → now
  │     ├─ loads existing rows by id in one query
  │     ├─ applies SyncMergePolicy per row
  │     ├─ appends to sync_changelog
  │     └─ IAppDbContext.SaveChangesAsync()
  │
  ├─ IdempotencyFilter (unwinding) ─ persists the response body against the key
  │
  └─ 200 Ok<SyncPushResponse>
```

### Why validation lives in an endpoint filter

FluentValidation validators are declared in Application next to the feature they guard, but they run
in an API-layer endpoint filter rather than a MediatR pipeline behaviour. One validation pass, one
place, and the failure is shaped as an HTTP concern (`400` with a field-keyed `errors` object)
without the Application layer needing to know what a status code is.

`GlobalExceptionHandler` still maps `FluentValidation.ValidationException` to the same `400` shape,
so a validator invoked from anywhere else degrades gracefully instead of returning `500`.

## Cross-cutting concerns

### Errors

`GlobalExceptionHandler` (registered via `AddExceptionHandler` + `AddProblemDetails`) translates
exceptions into RFC 9457 problem documents. Every failure the client can see has one envelope:

| Exception | Status | Title |
| --- | --- | --- |
| `ValidationException` | 400 | One or more validation errors occurred. *(adds an `errors` object)* |
| `BadHttpRequestException` | its own | Malformed request. |
| `AuthenticationFailedException` | 401 | Authentication failed. |
| `NotFoundException` | 404 | Resource not found. |
| `IdempotencyConflictException` | 409 | Idempotency key conflict. |
| `DbUpdateConcurrencyException` | 409 | The resource was modified concurrently. |
| `OperationCanceledException` | 499 | The request was cancelled. |
| anything else | 500 | An unexpected error occurred. |

Only 5xx responses log the exception; 4xx are expected traffic and log at information level. A
`traceId` extension is attached to every problem document for correlation.

### Time

Nothing calls `DateTimeOffset.UtcNow`. Handlers take `TimeProvider` and call `GetUtcNow()`, so tests
can substitute a fake clock and drive sync scenarios deterministically. `TimeProvider.System` is
registered in `AddInfrastructure`, guarded so a test registration already in the collection wins.

### Identifiers

Every client-owned key is a `Guid.CreateVersion7()`. UUIDv7 embeds a millisecond timestamp in its
high bits, so keys sort chronologically and inserts land at the right edge of the B-tree instead of
scattering across it — the practical difference between a healthy index and a fragmented one at
volume. It also means a device can mint a key offline with no coordination.

### Enum spelling

`SessionType.Focus90` must appear as `focus_90` in JSON, in the `session_type` column, and in a
generated TypeScript client. That single spelling is declared once, on the enum member:

```csharp
[JsonStringEnumMemberName("focus_90")]
Focus90 = 1,
```

`JsonStringEnumConverter` reads the attribute for the JSON side. `EnumWireConverter<T>` — via
`EnumWireNames<T>`, which reflects over the same attribute — reads it for the EF Core side. Storing
the string rather than the ordinal means renumbering a member cannot silently reinterpret existing
rows, and a row is readable straight out of `psql`. `EnumWireNamesTests` fails the build if the two
sides ever drift.

### Documentation into OpenAPI

.NET 9's OpenAPI generator does not read XML documentation. `XmlDocumentationStore` loads the `.xml`
files emitted beside the binaries and indexes them by compiler member id;
`XmlDocumentationSchemaTransformer` copies the text onto schema and property descriptions.

Records are the wrinkle: a positional record documents its members with `<param>` elements attached
to the *type*, not `<summary>` elements on the generated properties. The store indexes both shapes,
which is why the DTO contracts — all positional records — carry descriptions in the published
document and therefore into any generated client.

`BearerSecurityDocumentTransformer` declares the scheme once in `components`;
`BearerSecurityOperationTransformer` attaches it only to operations whose endpoint metadata actually
carries `IAuthorizeData`, so anonymous routes are not mislabelled as needing a token.

## Adding a feature

To add, say, `GET /api/v1/insights/streak`:

1. **Domain** — add entities or invariants only if the concept is genuinely new.
2. **Application/Contracts** — add the response record. Document every positional parameter with
   `<param>`; that prose becomes the OpenAPI schema description.
3. **Application/Features/Insights** — add `GetStreakQuery`, its handler, and a validator if it
   takes input. Depend on `IAppDbContext`, `ICurrentUser` and `TimeProvider`, nothing else.
4. **Api/Endpoints** — add `InsightsEndpoints` with a `MapGroup("/api/v1/insights")`, chain
   `.RequireAuthorization()`, `.WithSummary()`, `.Produces<T>()`, and
   `.AddEndpointFilter<ValidationFilter<TRequest>>()` if there is a body.
5. **Program.cs** — call `app.MapInsightsEndpoints()`.
6. **Infrastructure** — only if the schema changed; then add a configuration and a migration.

Handlers and validators are discovered by assembly scan in `AddApplication`, so no registration step
is needed for either.

## Apple identity verification

Two implementations of `IAppleIdentityTokenVerifier`, selected in `AddInfrastructure` by
`AppleAuth:UseStubVerification`. **Real verification is the default** — the stub has to be asked for
explicitly, so it cannot reach an environment by being forgotten.

`AppleJwksIdentityTokenVerifier` validates the signature, `iss`, `aud` and expiry. Keys come from
Apple's OIDC discovery document, which points at `https://appleid.apple.com/auth/keys`. The fetch,
cache and refresh belong to `ConfigurationManager<OpenIdConnectConfiguration>` rather than
hand-rolled caching, which buys automatic key rollover: when Apple signs with a `kid` we have not
seen, the manager re-fetches instead of failing the sign-in.

One distinction the code is careful about: **Apple being unreachable is not a bad credential.** A
failed key fetch raises `InvalidOperationException` (→ `500`), never
`AuthenticationFailedException` (→ `401`), so an upstream outage is never reported to the user as
"your token is invalid".

`StubAppleIdentityTokenVerifier` decodes the token and reads the same claims but performs **no
signature check**, and accepts any non-JWT string as a deterministic pseudo subject so a developer
can sign in from the simulator. It logs a warning on every call. `appsettings.Development.json` opts
into it; nothing else does.

## Guest accounts and linking

A first-time user gets an account before they get a registration prompt. `POST /auth/anonymous`
creates a `User` with `IsAnonymous = true` and no `AppleUserId`, provisions its settings row, and
returns the ordinary token pair. From the rest of the system's point of view a guest is just a user:
it owns rows, it syncs, it has settings.

`POST /auth/link-apple` upgrades it. The interesting case is when the Apple identity already belongs
to a registered account — then the registered account is authoritative, the guest's rows are
re-pointed onto it, and the guest is tombstoned. `LinkAppleAccountHandler` carries the full decision
table; [api-reference.md](api-reference.md) documents the client-visible contract, including the fact
that a merge changes the caller's user id.

Two invariants worth knowing:

- **Migrated rows get their `updatedAt` bumped to now.** The surviving account's other devices hold a
  sync cursor; rows arriving with their original, older stamps would sit permanently behind it and
  never be pulled.
- **Re-pointing an existing registration is refused, not merged.** Linking a second Apple identity
  onto an already-registered account would silently hand one person's history to another, so it
  raises `AccountConflictException` (→ `409`). `User.LinkApple` enforces the same rule at the domain
  level, so the invariant does not depend on the handler getting its checks in the right order.

## Project layout reference

| Path | Contents |
| --- | --- |
| `Domain/Common` | `EnumWireNames`, `ISyncEntity`, `SyncMergePolicy`, `SoundMixConfig` |
| `Domain/Entities` | The eight persisted entities |
| `Domain/Enums` | `SessionType`, `AudioCategory`, `SyncOperation` |
| `Application/Abstractions` | The four inverted interfaces |
| `Application/Common` | Typed exceptions, `SyncLimits` |
| `Application/Contracts` | Request/response DTOs — the OpenAPI schema surface |
| `Application/Features` | One folder per capability: command/query, validator, handler |
| `Infrastructure/Auth` | `JwtOptions`, `TokenService`, `StubAppleIdentityTokenVerifier` |
| `Infrastructure/Persistence` | `AppDbContext`, design-time factory, initializer |
| `Infrastructure/Persistence/Configurations` | Table, index, jsonb and constraint mapping |
| `Infrastructure/Persistence/Converters` | jsonb and enum value converters plus comparers |
| `Infrastructure/Persistence/Migrations` | `InitialCreate` and the model snapshot |
| `Api/Endpoints` | One static class per route group |
| `Api/Filters` | `ValidationFilter<T>`, `IdempotencyFilter` |
| `Api/Middleware` | `GlobalExceptionHandler` |
| `Api/OpenApi` | XML documentation store and the three transformers |
| `Api/Security` | `HttpContextCurrentUser` |

## Build conventions

`Directory.Build.props` applies to every project:

- `net9.0`, C# 13, nullable reference types on.
- `TreatWarningsAsErrors` — the build has zero warnings and is meant to stay that way.
- `GenerateDocumentationFile` — XML docs are required on public members, which is what feeds OpenAPI.
  Infrastructure suppresses `CS1591` because it is implementation detail, not client contract.
- `RollForward=LatestMajor` — see [getting-started.md](getting-started.md#a-note-on-net-10).

`Directory.Packages.props` centralises every package version, so a project file lists what it needs
but never which version.
