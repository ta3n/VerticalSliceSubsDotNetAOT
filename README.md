# Subscription Management — .NET 10, Vertical Slices, Native AOT

A subscription-billing API built with **Vertical Slice Architecture** on ASP.NET Core Minimal APIs, published as a **Native AOT** executable.
It started as a three-endpoint sample (create / get / cancel on an in-memory EF Core database) and was extended to feature parity with a
Spring Boot reference implementation: subscription lifecycle (pause / resume / renew), invoices, a plan catalog, keyset-paginated search,
RFC 9457 error responses, Redis caching, PostgreSQL persistence with versioned migrations, health probes and OpenTelemetry.

It also ships a complete **local Kubernetes platform** (kind): Helm chart behind a Kong gateway, a local image registry, Grafana/Prometheus/Loki/Tempo
monitoring, and GitOps with Argo CD plus canary releases through Argo Rollouts — all driven by [Taskfile.yml](Taskfile.yml) (`task up`).
See [Kubernetes](#kubernetes-kind--kong--helm).

## Architecture

```mermaid
flowchart LR
  C[Client] --> K[Kestrel HTTP]
  K --> M[Correlation + exception handling]
  M --> S["Slices: Subscriptions · Plans · Invoices"]
  S --> ST["Stores (Npgsql, explicit SQL)"]
  S --> CA[SubscriptionCache]
  ST --> PG[(PostgreSQL 17)]
  CA --> R[(Redis 7 / in-process fallback)]
  MG["Migrator (one-shot job; init container on k8s)"] --> PG
  K --> H["/health/live · /health/ready · /health"]
  K -.OTLP when configured.-> OT[(Collector)]
```

Each feature is a folder holding an `*Endpoint.cs` (route + binding) and an `*Handler.cs` (command/query record + logic). There is no mediator and no generic
repository: domain rules live in plain classes (`Subscription`, `Invoice`) and SQL lives in one `*Store` per table.
Design rationale: [docs/technical-design.md](docs/technical-design.md) · decisions: [docs/adr](docs/adr).

## Technology stack

| | Version |
|---|---|
| .NET SDK / runtime | 10.0.201 (pinned in `global.json`, `rollForward: latestFeature`) |
| ASP.NET Core | 10.0 — Minimal APIs, `CreateSlimBuilder`, source-generated JSON |
| PostgreSQL / driver | 17 / Npgsql 10.0.3 (no ORM) |
| Cache | Redis 7 via Microsoft.Extensions.Caching.StackExchangeRedis 10.0.12 (in-process fallback) |
| API docs | Microsoft.AspNetCore.OpenApi 10.0.12 + Scalar.AspNetCore 2.17.11 |
| Telemetry | OpenTelemetry 1.19.x (ASP.NET Core + Npgsql instrumentation, OTLP exporter) |
| Tests | xUnit 2.9.3, Testcontainers 4.15.0 (PostgreSQL, Redis), Microsoft.AspNetCore.Mvc.Testing 10.0.12 |
| Load tests | k6 (load + step-up stress), Locust — run in containers by `scripts/*.sh` |

Local Kubernetes platform (versions pinned in `Taskfile.yml` and `deploy/`):

| Component | Chart | App |
|---|---|---|
| kind / kubectl / Helm | — | kind 0.32 (node v1.36), Helm v4.3.1 (`task tools:helm`) |
| Kong Ingress Controller + Gateway (DB-less) | `kong/ingress` 0.24.0 | Kong 3.9, KIC 3.5 |
| Local registry | — | `registry:3` container `kind-registry` on `localhost:5001` |
| metrics-server | 3.14.0 | — |
| Prometheus, Alertmanager, Grafana | `kube-prometheus-stack` 92.11.0 | Prometheus 3.15, Grafana 13.2 |
| Loki / Tempo / Alloy | `grafana-community/loki` 19.2.0 / `grafana-community/tempo` 3.2.1 / `grafana/alloy` 1.14.3 | Loki 3.7, Tempo 3.1, Alloy 1.21 |
| Exporters (sidecars) | — | postgres_exporter v0.20.1, redis_exporter v1.96.0 |
| Argo CD / Argo Rollouts | `argo-cd` 10.11.1 / `argo-rollouts` 2.43.8 | v3.5.6 / v1.10.1 (+ kubectl plugin) |

## Prerequisites

* .NET SDK 10.0.201+ (`dotnet --version`)
* Docker (integration tests, the AOT smoke test and `docker compose`)
* For Native AOT publishing on Linux: `clang` and `zlib1g-dev` (`sudo apt-get install clang zlib1g-dev`).
  If you only have `gcc`, add `-p:CppCompilerAndLinker=gcc`.
* For the Kubernetes platform: [kind](https://kind.sigs.k8s.io), kubectl, [Task](https://taskfile.dev) v3 and Helm (`task tools:check`).
  Roughly 6–8 GB of free RAM for the whole stack (app + monitoring + Argo).

## Local development

```bash
docker compose up -d postgres redis                                   # backing services (localhost:5432 / :6379)
dotnet run --project src/SubscriptionManagement.Migrator -- \
  "Host=localhost;Database=subscriptions;Username=subs;Password=subs-dev-only"   # create/upgrade the schema
dotnet run --project src/SubscriptionManagement --launch-profile http # API on http://localhost:5127 (Scalar UI at /scalar)
```

Try the API with [SubscriptionManagement.http](SubscriptionManagement.http) (VS Code REST Client / Visual Studio).

### Endpoints

| Method | Path | Success | Notable errors |
|---|---|---|---|
| POST | `/subscriptions` | 201 + `Location` | 400 validation |
| GET | `/subscriptions/{id}` | 200 (cached) | 404 |
| DELETE | `/subscriptions/{id}` | 204 (row kept, status = 2) | 400 already canceled, 404 |
| POST | `/subscriptions/{id}/pause` · `/resume` | 200 | 409 invalid transition, 404 |
| POST | `/subscriptions/{id}/renew` | 200 `{subscription, invoice}` | 409, 404 |
| POST | `/subscriptions/search` | 200 `{items, nextCursor}` (keyset, default 20, max 100) | 400 bad status/cursor |
| GET | `/subscriptions/{id}/invoices` | 200 `{items}` newest first | 404 |
| GET · POST | `/invoices/{id}` · `/invoices/{id}/pay` | 200 | 404, 409 not payable |
| POST · GET | `/plans` · `/plans/{code}` · `/plans` (list) | 201 / 200 | 409 duplicate, 404 |
| GET | `/health/live` · `/health/ready` · `/health` | 200 `{"status":"UP"}` | 503 |

Subscription `status`: `1` Active, `2` Canceled, `3` Paused. Errors are `application/problem+json` (RFC 9457) with `type`
(`https://api.demo/errors/<slug>`), `title`, `status`, `detail`, `instance`, `timestamp`, `traceId` and, for validation, an `errors` map keyed by JSON field name.
Every response carries `X-Trace-Id` (an inbound value is honoured if it is a short printable token).

## Configuration

Standard ASP.NET Core configuration; environment variables use `__` for nesting.

| Key | Purpose | Default |
|---|---|---|
| `ConnectionStrings__Postgres` | **Required**; the app fails at startup without it | Development file: local compose DB |
| `ConnectionStrings__Redis` | Redis endpoint; empty ⇒ in-process cache | Development file: `localhost:6379` |
| `Cache__SubscriptionTtl` | Cached subscription lifetime | `00:05:00` |
| `OpenApi__Enabled` | Serve `/openapi/v1.json` and `/scalar` | `false` (`true` in Development) |
| `OTEL_EXPORTER_OTLP_ENDPOINT` (+ standard `OTEL_*`) | Enables trace/metric export | unset ⇒ telemetry off |
| `ASPNETCORE_URLS` / `ASPNETCORE_HTTP_PORTS` | Listening address (HTTP only) | 5127 (launch profile) / 8080 (container) |

The credentials in `appsettings.Development.json` and `compose.yaml` are local-development defaults only. Never reuse them; supply real values through
environment variables or your secret store. Without `POSTGRES_PASSWORD` set, compose uses the dev default.

## Database

Schema changes are versioned SQL files embedded in `src/SubscriptionManagement.Migrator/Migrations` (`V{n}__{description}.sql`, same tables as the Java reference).
The migrator applies pending files in order, records `version`/`checksum` in `schema_migrations`, refuses to run if an applied file was edited, and takes a PostgreSQL
advisory lock so concurrent runs are safe. The API never migrates on startup. In compose it runs as a one-shot service that the API waits for.
To add a migration, add `V5__what.sql`; never edit an applied one. Do not point this app at a database managed by the Java app's Flyway (different history table).

## Running tests

```bash
dotnet test                                                  # everything (needs Docker)
dotnet test tests/SubscriptionManagement.UnitTests           # fast, no Docker
dotnet test tests/SubscriptionManagement.IntegrationTests    # real PostgreSQL 17 + Redis 7 via Testcontainers
./scripts/aot-smoke.sh                                       # publish natively, run the native binaries, exercise the API
```

Integration tests host the real application with `WebApplicationFactory<Program>`; the schema is created by the real migrator; there is no EF InMemory and no mocking.
They cover every endpoint, error shapes, validation and malformed input, cache hits/eviction/TTL against real Redis and a Redis outage, concurrent cancels/payments/duplicate plans
(row locks), keyset paging with ties, correlation headers, health, OpenAPI on/off and migrator idempotence/tamper detection.

## Native AOT

**Why:** ~5× faster start, ~½ the memory and a ~61 MB image with no .NET runtime layer (see [docs/performance.md](docs/performance.md)).

**Constraints we accepted** (details: [docs/migration/native-aot-assessment.md](docs/migration/native-aot-assessment.md)):
* **EF Core is not used** — Microsoft documents EF Core Native AOT as experimental and advises against production use; data access is Npgsql + explicit SQL (ADR-002).
* No reflection-based libraries (FluentValidation replaced by explicit validators, ADR-004); every wire type is registered in `AppJsonSerializerContext`. **Adding an endpoint type ⇒ add a `[JsonSerializable]`.**
* Minimal APIs only (MVC is not AOT-compatible); HTTP only (`CreateSlimBuilder`), terminate TLS at the ingress.
* Analyzer warnings IL2026/IL2057/IL2067/IL2070/IL3050 are build errors; there are no suppressions.

```bash
dotnet publish src/SubscriptionManagement -c Release -r linux-x64 -o artifacts/api        # add -p:CppCompilerAndLinker=gcc if clang is absent
dotnet publish src/SubscriptionManagement.Migrator -c Release -r linux-x64 -o artifacts/migrator
ConnectionStrings__Postgres="Host=...;..." ./artifacts/api/SubscriptionManagement
./artifacts/api/SubscriptionManagement healthcheck            # exit 0/1; used by the container HEALTHCHECK
```

For a JIT build of the same code (e.g. to compare): `dotnet publish -p:PublishAot=false -p:EnableAotAnalyzer=false -p:EnableTrimAnalyzer=false`.

## Docker

```bash
podman compose up -d --build         # (or docker compose) postgres, redis, migrator (one-shot), api on http://localhost:8080
ENV_FILE=scripts/.env.compose scripts/load-test.sh   # k6 load test against localhost:8080 (VUS=, DURATION=, BASE_URL= to tune)
curl localhost:8080/health
podman compose down -v                 # also removes the database volume
```

Override host ports/credentials with `APP_PORT`, `POSTGRES_PORT`, `REDIS_PORT`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`, `OPENAPI_ENABLED`.
The images are multi-stage (`--target api|migrator`), built with clang, run on `runtime-deps:10.0-noble-chiseled` (no shell), as the non-root `app` user (UID 1654).

## Kubernetes (kind + Kong + Helm)

Everything runs in a single kind cluster and is driven by [Taskfile.yml](Taskfile.yml); `task --list` shows every task.

```mermaid
flowchart LR
  U["Host<br/>127.0.0.1:9080"] -- "kind port mapping<br/>or task kong:port-forward" --> KG
  subgraph kong [ns kong]
    KG[Kong gateway]
  end
  subgraph subscriptions [ns subscriptions]
    API["API pods (Rollout, HPA 2–4)<br/>init: migrator"] --> PG[("PostgreSQL 17<br/>StatefulSet + PVC")]
    API --> RD[(Redis 7)]
  end
  subgraph observability [ns observability]
    MON["Prometheus · Grafana · Loki · Tempo · Alloy"]
  end
  subgraph argo [ns argocd / argo-rollouts]
    AR["Argo CD · Argo Rollouts"]
  end
  KG -- "subs.localhost / host-less" --> API
  KG -- "grafana.localhost" --> MON
  KG -- "argocd.localhost · rollouts.localhost" --> AR
  REG[("kind-registry<br/>localhost:5001")] -. "images + OCI chart" .-> API
  REG -. "chart" .-> AR
```

### Quick start

```bash
task up                  # kind cluster → local registry → Kong → metrics-server → monitoring → Argo CD + Rollouts → build, push, publish → Argo CD sync → test
task cluster:ports       # is Kong published on 127.0.0.1:9080? If not (cluster not created from deploy/kind/cluster.yaml):
task kong:port-forward   #   keep this running in its own terminal; it reconnects by itself when the Kong pod restarts
task smoke               # requests through Kong on http://127.0.0.1:9080
task dev                 # after a code change: rebuild, push, publish the chart; Argo CD syncs it and Argo Rollouts canaries the API
task rollout:watch       # follow the canary live (rollout:promote / rollout:abort / rollout:retry to steer it)
task app:status          # pods, services, ingress, HPA, Rollout and AnalysisRuns of the release
task registry:images     # what is in the local registry (repository:tag + digest)
task load-test           # k6 through Kong; also stress-test, locust-test (settings: scripts/.env, e.g. VUS=200 task load-test)
task grafana:open        # Grafana URL + login
task argocd:open         # Argo CD + Rollouts dashboard URLs and the Argo CD admin password
task down                # remove the app, Argo, monitoring and Kong (cluster and registry kept);  task cluster:delete  removes the cluster
```

Other day-to-day tasks: `app:logs`, `app:migrator-logs`, `app:psql`, `app:restart`, `app:port-forward` (API on :8080, bypassing Kong),
`kong:routes`, `monitoring:status`, `app:purge` (also deletes the PostgreSQL volume).

### URLs and credentials

All UIs go through Kong on port 9080 (browsers and curl resolve `*.localhost` to 127.0.0.1, no `/etc/hosts` entry needed). Credentials are local-only defaults.

| What | URL | Login |
|---|---|---|
| API | `http://subs.localhost:9080` or `http://127.0.0.1:9080` (host-less rule) — Scalar UI at `/scalar` | — |
| Grafana | `http://grafana.localhost:9080` (`task grafana:port-forward` → `:3000`) | `admin` / `grafana-dev-only` |
| Argo CD | `http://argocd.localhost:9080` | `admin` / generated — `task argocd:open` prints it |
| Argo Rollouts dashboard | `http://rollouts.localhost:9080/rollouts/subscriptions` | — |
| Prometheus / Alertmanager | `task prometheus:port-forward` → `:9090`, `task alertmanager:port-forward` → `:9093` | — |
| PostgreSQL | `task app:psql` | `subs` / `subs-dev-only`, database `subscriptions` |

### Helm chart

[deploy/helm/subscription-management](deploy/helm/subscription-management) deploys:

* the API as a `Deployment`, or as an Argo `Rollout` when `rollout.enabled` (the local default). The migrator runs as an **init container**;
  its advisory lock makes concurrent replicas safe and the API never starts against an outdated schema;
* PostgreSQL 17 (StatefulSet + PVC) and Redis 7 (cache only), each with an optional exporter sidecar;
* an Ingress for `ingressClassName: kong` with KongPlugins: correlation-id on `X-Trace-Id`, rate-limiting, request-size-limiting (1 MB);
* HPA, PDB, NetworkPolicies (API from Kong only, Postgres/Redis from the API only, exporters from `observability` only);
* ServiceMonitor, PrometheusRule, a Grafana dashboard ConfigMap, an `AnalysisTemplate` for canaries, and a `helm test` pod.

Defaults are in `values.yaml`, kind overrides in `values-local.yaml`. The local overrides raise Kong's rate limit (load tests come from one IP)
and turn on OTLP export, exporters, monitoring resources, autoscaling (2–4 pods), network policies and the Rollout.
The chart refuses to render if `(maxReplicas + 1 surge) × config.postgresMaxPoolSize` exceeds `postgresql.maxConnections − 3`
(defaults: pool 30 per pod, `max_connections` 200), which would otherwise fail under load with `too many clients`.
Kong's own values are in [deploy/kong/values.yaml](deploy/kong/values.yaml) (NodePort 30080/30443, mapped to host 9080/9443 by [deploy/kind/cluster.yaml](deploy/kind/cluster.yaml)).

### Local image registry

Images go through a local registry ([deploy/registry](deploy/registry), the `kind-registry` container on `localhost:5001`, `task registry:setup`).
The host pushes to `localhost:5001/subscription-management[-migrator]`, and each kind node's containerd maps that name to the registry container over the
`kind` network. Deployments pin images by **digest**, so a rebuilt `dev` tag always rolls out. The registry also stores the OCI Helm chart that Argo CD syncs.

### Test scripts against the cluster

The scripts read [scripts/.env](scripts/.env): `BASE_URL=http://127.0.0.1:9080`, `DOCKER_CONTEXT=default` (the daemon hosting kind — the k6/Locust
containers use `--network host` there), `VUS`, `DURATION`, `LEVELS`, Locust settings. Explicit variables win (`VUS=200 task load-test`), and
`ENV_FILE=scripts/.env.compose` targets the compose stack instead. Taskfile loads the same file.

`kubectl port-forward` is a single tunnel and is the bottleneck under heavy load: keep `VUS` around 50–300 through it, or point the tests at the
node's NodePort directly (no port mapping needed on Linux):

```bash
BASE_URL=http://$(docker inspect -f '{{.NetworkSettings.Networks.kind.IPAddress}}' kind-control-plane):30080 task load-test
```

Measured on a laptop (2–4 API pods of 256 MiB, everything in one kind node): k6 through Kong ≈ 3.2–3.5k req/s with 0% errors; a canary rolled out
under that load (~509k requests) without a single failed request.

### GitOps and progressive delivery (Argo CD, Argo Rollouts)

`task argo:install` (part of `task up`) installs Argo CD ([deploy/argocd](deploy/argocd)) and Argo Rollouts ([deploy/argo-rollouts](deploy/argo-rollouts)).
The source of truth is an **OCI Helm chart in the local registry**, not a Git remote, so nothing leaves the machine:

```mermaid
flowchart LR
  D["task dev"] --> B["build + push images<br/>localhost:5001"]
  B --> P["task app:publish<br/>chart 0.1.0-dev.&lt;ts&gt; + values-release.yaml (image digests)"]
  P --> R[("kind-registry:5000/charts")]
  R -- "targetRevision >=0.1.0-0" --> A[Argo CD Application]
  A -- "sync (auto, self-heal, prune)" --> RO["Rollout (API)"]
  RO -- "25% → 50% → 75% → 100%" --> K["canary pods behind the same Service / Kong route"]
  RO -. "background AnalysisRun" .-> PR[(Prometheus)]
```

* Every publish is a new chart version that pins both image digests, so the version Argo CD shows identifies exactly what runs.
* With `rollout.enabled` (values-local) the API is a `Rollout`: canary steps 25/50/75% with pauses, weight = share of canary pods
  (no traffic router; Kong balances across all pods). An `AnalysisTemplate` queries Prometheus for the **canary pods only**
  (`rollouts_pod_template_hash`, set from the pod label into the OTel resource): 5xx ratio ≤ 1% and p95 < 300 ms, health probes excluded.
  One failed measurement aborts the rollout and the stable version keeps serving.
* `task app:deploy` (plain Helm) still works on a cluster without Argo CD; once the Application exists it refuses, because Argo CD would revert it.
  `task gitops:adopt` hands an existing Helm release over to Argo CD without deleting anything.

### Monitoring (Grafana, Prometheus, Loki, Tempo)

`task monitoring:install` (part of `task up`, after Kong) deploys [deploy/observability](deploy/observability) into the `observability` namespace:

```mermaid
flowchart LR
  K[Kong gateway] -- "OTLP spans (opentelemetry plugin)" --> A[Alloy]
  API[API pods] -- "OTLP traces + metrics" --> A
  A -- traces --> T[(Tempo)]
  A -- "metrics (remote write)" --> P[(Prometheus)]
  A -- "pod logs (/var/log/pods)" --> L[(Loki)]
  T -- "service graph + span metrics" --> P
  P -- "scrape" --> E["postgres_exporter · redis_exporter · Kong /metrics · kube-state-metrics · node-exporter"]
  G[Grafana] --> P & L & T
```

| | |
|---|---|
| Grafana | `http://grafana.localhost:9080` through Kong (`task grafana:open` prints the login), or `task grafana:port-forward` → `:3000` |
| Dashboards | *Subscription Management / Overview* (gateway vs API traffic and latency, 5xx, HPA, PostgreSQL, Redis, pod CPU/memory, logs, traces) — shipped by the app chart; *Community*: ASP.NET Core (19924/19925), Kong (7424), PostgreSQL (9628), Redis (763); plus the kube-prometheus-stack Kubernetes set |
| Traces | Kong span → ASP.NET Core → Npgsql in one trace (W3C `traceparent`); Tempo service map; latency exemplars and log `TraceId`s link to the trace |
| Alerts | `PrometheusRule` from the chart: API unavailable / 5xx > 5% / p95 > 500 ms, PostgreSQL down / connections > 80% / deadlocks, Redis down / memory > 90% (`task alertmanager:port-forward`) |
| Other UIs | `task prometheus:port-forward` (:9090), `task monitoring:status` (pods + scrape targets that are down) |

The app side is switched on in `values-local.yaml`: `config.otlpEndpoint` (Alloy), exporter sidecars (`postgresql.metrics`, `redis.metrics`),
`metrics.serviceMonitor`, `metrics.prometheusRule`, `metrics.grafanaDashboard`. With `networkPolicy.enabled`, only the `observability` namespace may scrape the exporters.

Sizing notes (laptop defaults): 10% of requests are traced (Kong `tracing_sampling_rate: 0.1` in deploy/kong/values.yaml; the API follows
the sampled flag). At 100%, a load test produced trace blocks whose compaction OOM-killed Tempo on every start; set 1.0 only while debugging.
Tempo's ingestion limits, Alloy's memory limiter and its small export queue shed spans under pressure instead of failing — metrics stay complete. Kong's 2xx/3xx access-log lines
are dropped before Loki (they are already counted and traced); 4xx/5xx and gateway errors are kept. Retention: Prometheus 3 d, Loki 72 h, Tempo 48 h.

## Project structure

```
src/SubscriptionManagement/            API: Program.cs, Features/{Subscriptions,Plans,Invoices}, Infrastructure/
src/SubscriptionManagement.Migrator/   SQL migration runner + embedded V*.sql
tests/SubscriptionManagement.UnitTests/         domain, validators, cursor codec, problem writer
tests/SubscriptionManagement.IntegrationTests/  Testcontainers + WebApplicationFactory
scripts/                               aot-smoke.sh, measure-startup.sh, k6 load/stress + Locust tests, .env (k8s) / .env.compose
deploy/helm/subscription-management/   Helm chart (API/Rollout, migrator, PostgreSQL, Redis, Kong plugins, monitoring, canary analysis)
deploy/kind/ · kong/ · registry/       kind cluster config, Kong values, local registry setup
deploy/observability/                  kube-prometheus-stack, Loki, Tempo, Alloy, Kong telemetry plugins
deploy/argocd/ · argo-rollouts/        Argo CD values, OCI repository + Application, Argo Rollouts values
Taskfile.yml                           every cluster/build/deploy/test command (task --list)
docs/                                  technical-design, implementation-plan, migration/*, adr/*, performance
```

## Observability

* **Logging:** structured JSON to stdout with scopes (`traceId`, request path). Unhandled errors are logged once with the trace id and returned as an opaque 500 problem.
* **Tracing/metrics:** set `OTEL_EXPORTER_OTLP_ENDPOINT` (and `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf` if your collector expects HTTP) to export ASP.NET Core and Npgsql telemetry. On Kubernetes this is wired to Grafana/Tempo/Prometheus — see [Monitoring](#monitoring-grafana-prometheus-loki-tempo).
* **Health:** `/health/live` (process), `/health/ready` (database — deliberately not Redis, as in the reference), `/health` (database + cache). Bodies contain only `{"status":"UP|DOWN"}`.
* **Correlation:** `X-Trace-Id` in/out, and as `traceId` in every problem response.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Publish fails: `Platform linker not found` / `clang` | `sudo apt-get install clang zlib1g-dev`, or `-p:CppCompilerAndLinker=gcc` |
| API exits: `ConnectionStrings:Postgres is not configured` | Set `ConnectionStrings__Postgres` (fail-fast by design) |
| `relation "subscriptions" does not exist` | Run the migrator first |
| Migrator: `Migration V2 ... was modified after it was applied` | An applied `V*.sql` was edited; restore it and add a new version instead |
| Native binary returns 500 with `JsonTypeInfo metadata ... was not provided` | A new request/response type is missing from `AppJsonSerializerContext` |
| `X-Trace-Id` missing from error responses in your own middleware | `UseExceptionHandler` clears response headers; set headers in `Response.OnStarting` (as `CorrelationMiddleware` does) |
| Integration tests: Docker errors | The tests need a running Docker daemon; `docker info` |
| Test overrides of `OpenApi:Enabled` ignored | `Program` reads it while building; use `WebApplicationFactory.UseSetting`, not `ConfigureAppConfiguration` |
| Old `https` launch profile missing | `CreateSlimBuilder` has no HTTPS; terminate TLS at the ingress or call `UseKestrelHttpsConfiguration` |
| Search/GET 500 on a row with a 29-digit price written by another system | Npgsql cannot read such `NUMERIC(57,28)` values into `decimal`; this API rejects prices ≥ 10^28 on create |
| `dotnet publish -p:PublishAot=false` reports IL2026 errors | The AOT analyzers are forced on in the csproj; add `-p:EnableAotAnalyzer=false -p:EnableTrimAnalyzer=false` for JIT-only publishes |

Kubernetes platform:

| Symptom | Cause / fix |
|---|---|
| `curl: (7) Failed to connect to 127.0.0.1 port 9080` | No port mapping (`task cluster:ports`) and no port-forward running, or the old one died when Kong restarted: run `task kong:port-forward` (it now reconnects) |
| Kong restarts / port-forward drops during a load test | Too many VUs through one `kubectl port-forward` tunnel: lower `VUS` or use the NodePort `BASE_URL` (see [Test scripts](#test-scripts-against-the-cluster)) |
| Images built but the cluster does not see them / kind not found | Docker CLI is on another context (e.g. `desktop-linux`); kind and the registry live on `default`. `DOCKER_CONTEXT=default` is set in `scripts/.env` |
| Port 9080/8000 already in use | Something else holds it (e.g. HTTP Toolkit on 8000, hence 9080); change `KONG_HTTP_PORT` in Taskfile.yml and `BASE_URL` in `scripts/.env` |
| 429 Too Many Requests in a load test | Kong rate-limiting per IP; `values-local.yaml` raises it — check `task kong:routes` |
| `sorry, too many clients already` | Pods × pool > `max_connections`; the chart guard should catch it — keep `config.postgresMaxPoolSize` and `postgresql.maxConnections` in step |
| `task app:deploy` refuses to run | The Argo CD Application owns the release; use `task dev` / `task gitops:deploy`, or `task app:uninstall` first |
| Canary aborted, AnalysisRun `MetricFailed` (`p95-latency` / `error-ratio`) | The canary pods breached `rollout.analysis.maxP95Seconds` / `maxErrorRatio`; `task rollout:status` shows the measured values. Fix and `task dev`, or `task rollout:retry` after a transient spike |
| Grafana: "Could not load plugin" | The Grafana container was OOM-killed (`kubectl -n observability describe pod -l app.kubernetes.io/name=grafana`); its limit is 1 GiB in `kube-prometheus-stack.yaml` |
| Tempo in CrashLoopBackOff (OOMKilled) after a big load test | Too many spans: keep Kong `tracing_sampling_rate` at 0.1; if it still loops, delete the Tempo PVC data (traces only) and restart it |
| `SubsPostgresDown` / `SubsRedisDown` firing | The exporter sidecar has no data (`absent()`), or the database is really down: `task app:status`, `task monitoring:status` |

## Compatibility notes vs. the original sample

Status codes and the six subscription response fields are unchanged. Differences: persistence is PostgreSQL (needs a database), errors are problem+json
(the old `{"error": ...}` text is preserved in `detail` and an `error` member), validation error keys are camelCase JSON names (previously PascalCase),
a missing `startDate` is now a 400 instead of silently binding to `0001-01-01`, and `status` may now be `3` (Paused). See
[docs/migration/java-feature-gap.md](docs/migration/java-feature-gap.md) §4.

## License

See [LICENSE](LICENSE).
