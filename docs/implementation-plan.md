# Implementation plan (living document)

## Phase 0 — Baseline
- [x] Inspect .NET repo (3 endpoints, EF InMemory, FluentValidation, no tests/Docker/CI)
- [x] Inspect Java reference
- [x] `dotnet restore` / `dotnet build -c Release` (0 warnings, 0 errors)
- [x] AOT/trim analyzer baseline (IL2026, IL3050 on EF Core; IL2026 on `MaxLength`)
- [x] Feature-gap, compatibility, AOT assessment, technical design, ADRs 001–004
- [ ] Baseline metrics of the original app (startup, RSS, publish size) — captured before restructuring

## Phase 1 — .NET 10 alignment
- [ ] `global.json`, `Directory.Build.props` (analyzers, warnings-as-errors for IL*)
- [ ] Solution `.slnx`; move API to `src/SubscriptionManagement`
- [ ] Remove EF Core / InMemory / FluentValidation

## Phase 2 — AOT foundation
- [ ] `CreateSlimBuilder`, `AppJsonSerializerContext`, `PublishAot`
- [ ] Problem-details pipeline + correlation middleware
- [ ] Npgsql data source + Migrator (V1–V4) + Testcontainers fixture
- [ ] Early native publish to prove toolchain and Npgsql

## Phase 3 — Feature porting
- [ ] Subscriptions: create, get (cache), cancel (lock), pause, resume, renew(+invoice), search
- [ ] Plans: create, get, list
- [ ] Invoices: get, pay, list by subscription
- [ ] Redis/in-memory cache, health checks, OpenTelemetry, OpenAPI

## Phase 4 — Tests
- [ ] Unit tests (domain, validators, cursor)
- [ ] Integration tests (Testcontainers Postgres + Redis) incl. negative and concurrency
- [ ] AOT smoke script

## Phase 5 — Performance
- [ ] Startup / RSS / size: original JIT vs new JIT vs new AOT (others marked Not measured)

## Phase 6 — Deployment & docs
- [ ] Dockerfile (AOT), compose.yaml; build and run container
- [ ] README rewrite, native-aot-assessment result section, `.http` update
- [ ] Final review: build/test/publish, vulnerability scan (`dotnet list package --vulnerable`), secret scan
