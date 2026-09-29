# Java reference vs .NET: feature gap analysis

Reference: `/home/tanguyen/IdeaProjects/demo` (Spring Boot 4.1.1, Java 25, Postgres + Flyway, Redis cache).
Target: this repository. Both were read from source; the READMEs/design docs were not trusted over the code.

> **Premise correction.** The task brief describes a ".NET 8" application. The repository is already
> `net10.0` (`SubscriptionManagement.csproj`, EF Core 10.0.3, FluentValidation 12.1.1). It also has **no tests,
> no Dockerfile, no CI, and uses `UseInMemoryDatabase`**. The migration therefore reduces to an audit of
> .NET 10 compatibility (see `dotnet10-compatibility.md`) plus the AOT work and the Java gap.

## 1. Baseline of the current .NET app

| Item | Finding |
|---|---|
| Endpoints | `POST /subscriptions` (201), `GET /subscriptions/{id}` (200/404), `DELETE /subscriptions/{id}` (204/404/400) |
| Persistence | EF Core InMemory (non-durable, no transactions, no concurrency control) |
| Validation | FluentValidation (name required ≤100, price > 0, trialEndsAt > startDate) |
| Errors | `Results.ValidationProblem` (400), ad-hoc `{ "error": "Subscription already canceled." }` (400), bare 404 |
| Status enum | `Active = 1`, `Canceled = 2`, serialized as a number |
| Auth / caching / messaging / health / OTel | none |
| `dotnet build` | 0 warnings JIT; with AOT/trim analyzers: IL2026 (`MaxLength`), IL2026 + IL3050 (`DbContext`) |

## 2. Java behaviours worth noting

* **Money**: `BigDecimal`, `subscriptions.price NUMERIC(57,28)`; invoices/plans `NUMERIC(20,4)`.
* **Concurrency**: `SELECT … FOR UPDATE` (pessimistic write lock) before every state change, so two overlapping
  cancels serialize (one 204, one 400) — covered by `PostgresCancellationConcurrencyTests`.
* **Cache**: `@Cacheable` on get, `@CachePut` on create/pause/resume/renew, `@CacheEvict` on cancel; key = subscription id,
  TTL 5 min, null values not cached; in-memory when Redis is not configured.
* **Errors**: RFC 9457 `application/problem+json` for everything; `type = https://api.demo/errors/<slug>`, plus `instance`,
  `timestamp`, `traceId`; validation errors carry an `errors` map (`field → [messages]`); unexpected → opaque 500.
* **Correlation**: `X-Trace-Id` header honoured/echoed/generated.
* **Health**: actuator liveness/readiness probes; DB and Redis health indicators.
* **Search**: keyset pagination on `(startDate DESC, id DESC)`, opaque base64url cursor `startDate|id`, default limit 20, max 100
  (out-of-range → default, not an error), optional status filter (`ACTIVE|CANCELED|PAUSED`).
* **Lifecycle**: `pause` (ACTIVE→PAUSED), `resume` (PAUSED→ACTIVE), `renew` (ACTIVE only; advances period 1 month, issues OPEN invoice in USD
  at current price); illegal transition → 409.
* **Invoices**: `OPEN(1)/PAID(2)/VOID(3)`; pay only when OPEN (409 otherwise), row-locked.
* **Plans**: catalog (`code` PK), seeded `free`, `pro-monthly`, `pro-yearly`; get returns only active plans; duplicate code → 409.
* **Schema**: Flyway V1–V4 (V4 adds `current_period_start/end` with backfill, internal — not in API response).

## 3. Capability matrix

| Capability | Java | Current .NET | Required | Proposed .NET implementation |
|---|---|---|---|---|
| Create subscription | ✔ | Implemented | Keep | Minimal API slice; explicit validation; INSERT via Npgsql |
| Get subscription | ✔ (cached) | Implemented (uncached) | Keep + cache | Cache-aside via `IDistributedCache` |
| Cancel subscription | ✔ row lock | Implemented (no lock) | Keep + lock | `BEGIN; SELECT … FOR UPDATE; UPDATE; COMMIT` |
| Pause / Resume | ✔ | **Missing** | Yes | New slices; `Paused = 3` |
| Renew + invoice | ✔ | **Missing** | Yes | New slice; one transaction updates period and inserts invoice |
| Search (keyset pagination) | ✔ | **Missing** | Yes | `POST /subscriptions/search`, same cursor format |
| Plans (create/get/list) | ✔ | **Missing** | Yes | New slices, seeded by migration |
| Invoices (get/pay/list) | ✔ | **Missing** | Yes | New slices |
| RFC 9457 problem+json, `traceId`, `timestamp`, `instance` | ✔ | Partial (validation only) | Yes | `IExceptionHandler` + `IProblemDetailsService`, source-generated JSON |
| Domain error types (`type` URIs) | ✔ | Missing | Yes | `ApiProblem` exception carrying type/title/status/extensions |
| Correlation id header | ✔ | Missing | Yes | Middleware; also sets `Activity` tag / log scope |
| Persistent DB (PostgreSQL) | ✔ | Missing (InMemory) | Yes | Npgsql + SQL (ADR-002) |
| Schema migrations | ✔ Flyway | Missing | Yes | Versioned SQL runner, dedicated executable (ADR-003) |
| Redis cache w/ TTL 5 min | ✔ | Missing | Yes | `Microsoft.Extensions.Caching.StackExchangeRedis`; in-memory fallback |
| Health probes (live/ready, DB/Redis checks) | ✔ | Missing | Yes | `MapHealthChecks` (`/health/live`, `/health/ready`) |
| Bean-validation on Plans/Subscriptions | ✔ | FluentValidation | Yes (equivalent) | Hand-written validators (FluentValidation is reflection/expression based — ADR-004) |
| OpenAPI + Scalar UI | ✔ | Missing | Should | `Microsoft.AspNetCore.OpenApi` (AOT-supported); Scalar only if it publishes warning-free |
| Docker / compose | ✔ | Missing | Yes | AOT Dockerfile + `compose.yaml` (app, migrator, postgres, redis) |
| Tests (MockMvc + Postgres) | ✔ | **None** | Yes | xUnit + `WebApplicationFactory` + Testcontainers Postgres/Redis |
| Spring Security `denyAll` for unknown routes, CSRF exemptions, stateless session | ✔ | n/a | Not applicable | ASP.NET returns 404 for unmapped routes; no cookies/sessions ⇒ no CSRF surface. No authentication existed in either app. |
| H2 console, devtools, docker-compose auto-start, Lombok, springdoc annotations | ✔ | n/a | Should not be ported | Java-tooling conveniences with no .NET equivalent value |
| `SchemaDraftExporterTest` (DDL from JPA metadata) | ✔ | n/a | Should not be ported | No ORM model in the .NET design; SQL is authored directly |
| Message brokers, scheduling, retries/resilience, gRPC, outbound HTTP | ✘ | ✘ | Not applicable | Neither app has them; not invented |

## 4. Deliberate behaviour decisions (compatibility impact)

| Area | Old .NET | New .NET | Reason / impact |
|---|---|---|---|
| Double cancel body | `{"error":"Subscription already canceled."}` | problem+json with `detail` = same text **and** an `error` member with the same text | Status (400) and message preserved; extra members are additive |
| Validation body keys | FluentValidation names (`ServiceName`) | JSON names (`serviceName`) | Matches Java and the request body the client sends; documented breaking-ish change for clients parsing keys |
| 404 body | empty | problem+json | Additive |
| Unknown status filter/cursor | n/a | 400 problem | Java parity |
| `startDate` missing | binds to `0001-01-01` | 400 (required) | Java parity, fixes silent bad data |
| Status numbers | 1/2 | 1/2/3 (`Paused`) | Additive |
| Persistence | InMemory | PostgreSQL | Required by parity; local run needs Postgres (compose provided) |
| `price` type | `decimal` | `decimal` (`NUMERIC(57,28)` column, as Java) | .NET `decimal` fits within it |
