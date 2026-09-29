# Implementation plan (living document — final state)

## Phase 0 — Baseline
- [x] Inspect .NET repo (3 endpoints, EF InMemory, FluentValidation, no tests/Docker/CI)
- [x] Inspect Java reference
- [x] `dotnet restore` / `dotnet build -c Release` (0 warnings, 0 errors)
- [x] AOT/trim analyzer baseline (IL2026, IL3050 on EF Core; IL2026 on `MaxLength`)
- [x] Feature-gap, compatibility, AOT assessment, technical design, ADRs 001–004
- [x] Baseline metrics of the original app (`docs/performance.md`); original app under AOT publishes but returns 500

## Phase 1 — .NET 10 alignment
- [x] `global.json`, `Directory.Build.props` (nullable, warnings as errors); AOT/trim analyzers on in the API csproj
- [x] Solution `.slnx`; API moved to `src/SubscriptionManagement`
- [x] Remove EF Core / InMemory / FluentValidation
- [x] (Finding) the repo was already `net10.0`: no 8→10 upgrade was needed

## Phase 2 — AOT foundation
- [x] `CreateSlimBuilder`, `AppJsonSerializerContext`, `PublishAot`
- [x] Problem-details pipeline + correlation middleware
- [x] Npgsql data source + Migrator (V1–V4) + Testcontainers fixture
- [x] Early native publish (proved toolchain: gcc works, clang absent locally)

## Phase 3 — Feature porting
- [x] Subscriptions: create, get (cache), cancel (row lock), pause, resume, renew (+invoice), search
- [x] Plans: create, get, list
- [x] Invoices: get, pay, list by subscription
- [x] Redis / in-process cache, health checks, OpenTelemetry, OpenAPI + Scalar

## Phase 4 — Tests
- [x] Unit tests (53): domain state machines, validators, cursor codec, problem writer, status mapping
- [x] Integration tests (83) on real PostgreSQL 17 + Redis 7, negative and concurrency cases
- [x] AOT smoke script (`scripts/aot-smoke.sh`, 37 checks on the native binaries)

## Phase 5 — Performance
- [x] Startup / RSS / size / latency / throughput: original JIT vs new JIT vs new AOT (.NET 8 column "Not measured" — no .NET 8 build exists)
- [ ] Controlled benchmark on isolated hardware (only indicative numbers were taken)

## Phase 6 — Deployment & docs
- [x] Dockerfile (AOT, chiseled, non-root), `compose.yaml`; built and ran the full stack, container healthy
- [x] README rewrite, native-aot-assessment result, `.http` update, performance doc
- [x] Final review: Release build (0 warnings), 136 tests, native publish (0 warnings), smoke test, `dotnet list package --vulnerable` (none), secret scan (dev-only defaults only)

## Not done / follow-ups
- [ ] Run the GitHub Actions workflow (`.github/workflows/ci.yml`) — written but never executed
- [ ] Publish for other RIDs (`linux-arm64`, `win-x64`) — only `linux-x64` verified
- [ ] Migrate tests from `xunit` 2.9.3 (flagged "Legacy" by NuGet) to `xunit.v3`
- [ ] Authentication/authorization — none existed in either app; out of scope without a business requirement
- [ ] Revisit EF Core when Microsoft declares Native AOT support production-ready
